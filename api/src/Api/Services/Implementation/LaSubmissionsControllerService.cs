using Api.Dtos;
using Api.Services.Abstract;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Queries;
using Application.Common.Permissions;
using Application.Submissions;
using Domain.Collections;
using Domain.Organisations;
using Domain.Users;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;

namespace Api.Services.Implementation;

// Read side of the LA-facing submission history + detail (BLUM-6097 / BLUM-6098). Mirrors
// DhscSubmissionsControllerService but takes the organisation from the caller's Permission (never the route), so an
// LA only ever sees its own submissions. Reads stay viewable after a window closes (a finalised report is the
// immutable record); the org-scoping and status derivation are shared with the DHSC surface.
//
// The history lists both submission flows (BLUM-7859 AC1): quarterly reports from `submissions` and maturity
// assessments from `maturity_assessments`, reduced to the same "started, and submitted at" shape. The maturity
// detail's sections are served by maturity-assessment/{id}/submitted; this detail only carries the header.
public class LaSubmissionsControllerService(
    ICollectionQueries collectionQueries,
    ISubmissionQueries submissionQueries,
    IMaturityAssessmentQueries maturityAssessmentQueries,
    IOrganisationQueries organisationQueries,
    IUserQueries userQueries,
    IDateTimeProvider dateTimeProvider,
    IQuarterlyReportExportService quarterlyExportService,
    IMaturityAssessmentExportService maturityExportService,
    IReportExportSizeService exportSizeService) : ILaSubmissionsControllerService
{
    public async Task<IReadOnlyList<MyCollectionSummaryDto>> GetMyCollectionsAsync(
        Permission permission,
        CancellationToken cancellationToken)
    {
        var utcNow = dateTimeProvider.GetUtcNow();

        var organisationId = ResolveOrg(permission);
        if (organisationId is null)
        {
            return [];
        }

        var organisationLookup = await organisationQueries.GetOrganisationById(organisationId, cancellationToken);
        if (organisationLookup.IsNone)
        {
            return [];
        }

        var organisation = organisationLookup.ValueUnsafe();

        // Candidate set = the org's assigned collections (coarse prefilter, already non-deleted) UNION any collection
        // the org has a submission for — so a submitted-but-since-unassigned period is never silently dropped from the
        // history (the LA analogue of DHSC's ResolveReportableOrganisations). Both sides are single reads.
        var assigned = await collectionQueries.GetAssignedToOrganisationAsync(organisationId, cancellationToken);
        var submitted = await collectionQueries.GetSubmittedByOrganisationAsync(organisationId, cancellationToken);
        var submittedAtByCollection = await GetStartedRowsAsync(organisationId, cancellationToken);

        var candidates = new Dictionary<Guid, Collection>();
        foreach (var collection in assigned)
        {
            candidates[collection.Id.Value] = collection;
        }

        foreach (var collection in submitted)
        {
            candidates.TryAdd(collection.Id.Value, collection);
        }

        return candidates.Values
            // Published windows with a submission flow: drafts are not reportable (mirrors the DHSC list filter).
            .Where(collection => HasSubmissionFlow(collection) && !collection.IsDraft)
            // Keep the authoritative type-aware assignment gate, but never drop a window the org actually submitted to.
            .Where(collection => SubmissionEligibility.IsAssignedTo(collection, organisation) || submittedAtByCollection.ContainsKey(collection.Id.Value))
            .Select(collection =>
            {
                var hasSubmission = submittedAtByCollection.TryGetValue(collection.Id.Value, out var submittedAt);
                var status = SubmissionStatusRules.Derive(hasSubmission, submittedAt, collection.SubmissionDeadline, utcNow);

                return new MyCollectionSummaryDto(
                    collection.Id.Value,
                    collection.Type?.ToString(),
                    collection.Quarter,
                    collection.ReportingPeriodStart,
                    collection.ReportingPeriodEnd,
                    collection.SubmissionDeadline,
                    collection.GetStatus(utcNow).ToString(),
                    status,
                    submittedAt,
                    collection.FinancialYear);
            })
            // Most recent reporting period first; the frontend table re-sorts on demand. DateOnly? — nulls last.
            .OrderByDescending(dto => dto.ReportingPeriodStart ?? DateOnly.MinValue)
            .ThenByDescending(dto => dto.Quarter ?? 0)
            .ToList();
    }

    public async Task<Option<MySubmissionDetailDto>> GetMySubmissionDetailAsync(
        Permission permission,
        Guid collectionId,
        CancellationToken cancellationToken)
    {
        var organisationId = ResolveOrg(permission);
        if (organisationId is null)
        {
            return Option<MySubmissionDetailDto>.None;
        }

        var organisationLookup = await organisationQueries.GetOrganisationById(organisationId, cancellationToken);
        if (organisationLookup.IsNone)
        {
            return Option<MySubmissionDetailDto>.None;
        }

        var organisation = organisationLookup.ValueUnsafe();

        var lookup = await collectionQueries.GetByIdAsync(new CollectionId(collectionId), includeDeleted: false, cancellationToken);
        if (lookup.IsNone)
        {
            return Option<MySubmissionDetailDto>.None;
        }

        var collection = lookup.ValueUnsafe();

        // Published-only: an unpublished draft must not leak its setup metadata before publication (the list path
        // applies the same !IsDraft filter).
        if (!HasSubmissionFlow(collection) || collection.IsDraft)
        {
            return Option<MySubmissionDetailDto>.None;
        }

        var detail = await GetDetailAsync(collection, organisation, cancellationToken);
        var hasSubmission = detail is not null;

        // 404 for a collection the caller's org is neither assigned to nor has a submission for — prevents probing
        // arbitrary collection ids. An assigned org with no submission still shows its derived (not started/overdue)
        // status.
        if (!SubmissionEligibility.IsAssignedTo(collection, organisation) && !hasSubmission)
        {
            return Option<MySubmissionDetailDto>.None;
        }

        var (submittedByName, submittedByEmail) = await ResolveSubmitterAsync(detail?.SubmittedBy, cancellationToken);
        var submittedAt = detail?.SubmittedAt;
        var status = SubmissionStatusRules.Derive(hasSubmission, submittedAt, collection.SubmissionDeadline, dateTimeProvider.GetUtcNow());

        // Last, so every read above has finished: the measurement is the download endpoint's own call for this
        // collection's flow, and only a finalised report has a file to measure (BLUM-9018). The quarterly one skips the
        // download's live-render fallback, so a legacy submission with no frozen snapshot shows the format alone rather
        // than re-rendering its whole report on every view.
        long? exportSizeBytes = null;
        if (submittedAt is { } finalisedAt)
        {
            exportSizeBytes = await exportSizeService.MeasureAsync(
                new ReportExportSizeKey("la", collectionId, organisationId.Value, finalisedAt),
                () => SubmissionEligibility.IsMaturityAssessment(collection)
                    ? maturityExportService.GenerateAsync(permission, collectionId, cancellationToken)
                    : quarterlyExportService.GenerateFromFrozenSnapshotAsync(permission, collectionId, cancellationToken));
        }

        return new MySubmissionDetailDto(
            collection.Id.Value,
            collection.Quarter,
            collection.ReportingPeriodStart,
            collection.ReportingPeriodEnd,
            organisation.Name,
            status,
            hasSubmission,
            submittedByName,
            submittedByEmail,
            submittedAt,
            collection.Type?.ToString(),
            collection.FinancialYear,
            detail?.ReferenceNumber,
            exportSizeBytes);
    }

    private static bool HasSubmissionFlow(Collection collection) =>
        SubmissionEligibility.IsQuarterly(collection) || SubmissionEligibility.IsMaturityAssessment(collection);

    // IsNone/ValueUnsafe, not Match — Option.Match throws on a null result (see ActivitiesControllerService.ResolveOrg).
    private static OrganisationId? ResolveOrg(Permission permission) =>
        permission.OrganisationId.IsNone ? null : permission.OrganisationId.ValueUnsafe();

    // Row presence = started; SubmittedAt = finalised. A collection has exactly one flow, so the two reads never
    // collide on a key; TryAdd is belt-and-braces.
    private async Task<Dictionary<Guid, DateTime?>> GetStartedRowsAsync(OrganisationId organisationId, CancellationToken cancellationToken)
    {
        var rows = new Dictionary<Guid, DateTime?>();
        foreach (var submission in await submissionQueries.GetForOrganisationAsync(organisationId, cancellationToken))
        {
            rows[submission.CollectionId] = submission.SubmittedAt;
        }

        foreach (var assessment in await maturityAssessmentQueries.GetForOrganisationAsync(organisationId, cancellationToken))
        {
            rows.TryAdd(assessment.CollectionId, assessment.SubmittedAt);
        }

        return rows;
    }

    // Null when the organisation has not started anything for this window.
    private async Task<SubmissionDetail?> GetDetailAsync(Collection collection, Organisation organisation, CancellationToken cancellationToken)
    {
        if (SubmissionEligibility.IsMaturityAssessment(collection))
        {
            var assessmentLookup = await maturityAssessmentQueries.GetDetailForCollectionAndOrgAsync(collection.Id, organisation.Id, cancellationToken);
            if (assessmentLookup.IsNone)
            {
                return null;
            }

            var assessment = assessmentLookup.ValueUnsafe();
            return new SubmissionDetail(assessment.SubmittedAt, assessment.SubmittedBy, assessment.ReferenceNumber);
        }

        var submissionLookup = await submissionQueries.GetDetailForCollectionAndOrgAsync(collection.Id, organisation.Id, cancellationToken);
        if (submissionLookup.IsNone)
        {
            return null;
        }

        var submission = submissionLookup.ValueUnsafe();
        return new SubmissionDetail(submission.SubmittedAt, submission.SubmittedBy, submission.ReferenceNumber);
    }

    private async Task<(string? Name, string? Email)> ResolveSubmitterAsync(Guid? submittedBy, CancellationToken cancellationToken)
    {
        if (submittedBy is not { } userId)
        {
            return (null, null);
        }

        var userLookup = await userQueries.FindById(new UserId(userId), cancellationToken);
        if (userLookup.IsNone)
        {
            return (null, null);
        }

        var user = userLookup.ValueUnsafe();
        return (user.Name.ToString(), user.Email);
    }

    private sealed record SubmissionDetail(DateTime? SubmittedAt, Guid? SubmittedBy, string? ReferenceNumber);
}