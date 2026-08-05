using Api.Dtos;
using Api.Services.Abstract;
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
public class LaSubmissionsControllerService(
    ICollectionQueries collectionQueries,
    ISubmissionQueries submissionQueries,
    IOrganisationQueries organisationQueries,
    IUserQueries userQueries) : ILaSubmissionsControllerService
{
    public async Task<IReadOnlyList<MyCollectionSummaryDto>> GetMyCollectionsAsync(
        Permission permission,
        CancellationToken cancellationToken)
    {
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
        var submissionRows = await submissionQueries.GetForOrganisationAsync(organisationId, cancellationToken);
        var submissionByCollection = submissionRows.ToDictionary(r => r.CollectionId);

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
            // Quarterly-only published windows: drafts are not reportable, and maturity assessments have no submission
            // flow (mirrors the DHSC list filter and QuarterlyReportExportService).
            .Where(collection => SubmissionEligibility.IsQuarterly(collection) && !collection.IsDraft)
            // Keep the authoritative type-aware assignment gate, but never drop a window the org actually submitted to.
            .Where(collection => SubmissionEligibility.IsAssignedTo(collection, organisation) || submissionByCollection.ContainsKey(collection.Id.Value))
            .Select(collection =>
            {
                var hasSubmission = submissionByCollection.TryGetValue(collection.Id.Value, out var row);
                var submittedAt = hasSubmission ? row.SubmittedAt : null;
                var status = SubmissionStatusRules.Derive(hasSubmission, submittedAt, collection.SubmissionDeadline);

                return new MyCollectionSummaryDto(
                    collection.Id.Value,
                    collection.Type?.ToString(),
                    collection.Quarter,
                    collection.ReportingPeriodStart,
                    collection.ReportingPeriodEnd,
                    collection.SubmissionDeadline,
                    collection.GetStatus().ToString(),
                    status,
                    submittedAt);
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

        // Quarterly-only, published-only: a maturity-assessment window has no submission detail, and an unpublished
        // draft must not leak its setup metadata before publication (the list path applies the same !IsDraft filter).
        if (!SubmissionEligibility.IsQuarterly(collection) || collection.IsDraft)
        {
            return Option<MySubmissionDetailDto>.None;
        }

        var detailLookup = await submissionQueries.GetDetailForCollectionAndOrgAsync(collection.Id, organisation.Id, cancellationToken);
        var hasSubmission = detailLookup.IsSome;

        // 404 for a collection the caller's org is neither assigned to nor has a submission for — prevents probing
        // arbitrary collection ids. An assigned org with no submission still shows its derived (not started/overdue)
        // status.
        if (!SubmissionEligibility.IsAssignedTo(collection, organisation) && !hasSubmission)
        {
            return Option<MySubmissionDetailDto>.None;
        }

        var detail = hasSubmission ? detailLookup.ValueUnsafe() : default;

        string? submittedByName = null;
        string? submittedByEmail = null;
        if (hasSubmission && detail.SubmittedBy is { } submittedBy)
        {
            var userLookup = await userQueries.FindById(new UserId(submittedBy), cancellationToken);
            if (userLookup.IsSome)
            {
                var user = userLookup.ValueUnsafe();
                submittedByName = user.Name.ToString();
                submittedByEmail = user.Email;
            }
        }

        var submittedAt = hasSubmission ? detail.SubmittedAt : null;
        var status = SubmissionStatusRules.Derive(hasSubmission, submittedAt, collection.SubmissionDeadline);

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
            submittedAt);
    }

    // IsNone/ValueUnsafe, not Match — Option.Match throws on a null result (see ActivitiesControllerService.ResolveOrg).
    private static OrganisationId? ResolveOrg(Permission permission) =>
        permission.OrganisationId.IsNone ? null : permission.OrganisationId.ValueUnsafe();
}