using Api.Dtos;
using Api.Services.Abstract;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Queries;
using Application.MaturityAssessments;
using Application.MaturityAssessments.Protocol;
using Application.MaturityAssessments.Snapshots;
using Application.Submissions;
using Domain.Collections;
using Domain.Organisations;
using Domain.Users;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;

namespace Api.Services.Implementation;

// The status list never measures a downloadable ZIP: building one per organisation on every load is what made the
// read expensive enough to matter (BLUM-7216). The detail screen measures its one organisation's ZIP to caption the
// download (BLUM-9018), through IReportExportSizeService, which caches it and cannot fail the read.
//
// Two submission flows share these screens. A quarterly window's rows live in `submissions`; a maturity-assessment
// window's live in `maturity_assessments` (BLUM-7855 AC4 / BLUM-7859 AC2). Both are reduced to the same
// "started, and submitted at" shape here so the status derivation, the assignment resolution and the DTOs stay one
// code path. The maturity detail additionally carries its section statuses and the frozen snapshot, because DHSC
// reads a maturity assessment inline rather than downloading a per-section ZIP.
public class DhscSubmissionsControllerService(
    ICollectionQueries collectionQueries,
    ISubmissionQueries submissionQueries,
    IMaturityAssessmentQueries maturityAssessmentQueries,
    IOrganisationQueries organisationQueries,
    IUserQueries userQueries,
    IDateTimeProvider dateTimeProvider,
    IQuarterlyReportExportService quarterlyExportService,
    IMaturityAssessmentExportService maturityExportService,
    IReportExportSizeService exportSizeService) : IDhscSubmissionsControllerService
{
    public async Task<Option<CollectionSubmissionSummaryDto>> GetOrgStatusesAsync(
        Guid collectionId,
        CancellationToken cancellationToken)
    {
        var utcNow = dateTimeProvider.GetUtcNow();

        var lookup = await collectionQueries.GetByIdAsync(new CollectionId(collectionId), includeDeleted: false, cancellationToken);
        if (lookup.IsNone)
        {
            return Option<CollectionSubmissionSummaryDto>.None;
        }

        var collection = lookup.ValueUnsafe();
        if (!HasSubmissionFlow(collection))
        {
            return Option<CollectionSubmissionSummaryDto>.None;
        }

        var allOrganisations = await organisationQueries.GetOrganisations(cancellationToken);
        var submittedAtByOrg = await GetStartedRowsAsync(collection, cancellationToken);

        // Include every assigned org plus any org that has a submission row even if no longer assigned, so its data is
        // never silently dropped from the status list.
        var reportable = SubmissionAssignment.ResolveReportableOrganisations(collection, allOrganisations, submittedAtByOrg.Keys);

        var organisations = reportable
            .OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
            .Select(o =>
            {
                var hasSubmission = submittedAtByOrg.TryGetValue(o.Id.Value, out var submittedAt);
                var status = SubmissionStatusRules.Derive(hasSubmission, submittedAt, collection.SubmissionDeadline, utcNow);

                return new OrgSubmissionStatusDto(o.Id.Value, o.Name, status, submittedAt);
            })
            .ToList();

        return new CollectionSubmissionSummaryDto(
            collection.Id.Value,
            collection.Name,
            collection.GetStatus(utcNow).ToString(),
            collection.CollectionOpenDate,
            collection.SubmissionDeadline,
            collection.Quarter,
            collection.ReportingPeriodStart,
            collection.ReportingPeriodEnd,
            organisations,
            collection.Type?.ToString(),
            collection.FinancialYear);
    }

    public async Task<Option<OrgSubmissionDetailDto>> GetOrgDetailAsync(
        Guid collectionId,
        Guid organisationId,
        CancellationToken cancellationToken)
    {
        var utcNow = dateTimeProvider.GetUtcNow();

        var lookup = await collectionQueries.GetByIdAsync(new CollectionId(collectionId), includeDeleted: false, cancellationToken);
        if (lookup.IsNone)
        {
            return Option<OrgSubmissionDetailDto>.None;
        }

        var collection = lookup.ValueUnsafe();
        if (!HasSubmissionFlow(collection))
        {
            return Option<OrgSubmissionDetailDto>.None;
        }

        var organisationLookup = await organisationQueries.GetOrganisationById(new OrganisationId(organisationId), cancellationToken);
        if (organisationLookup.IsNone)
        {
            return Option<OrgSubmissionDetailDto>.None;
        }

        var organisation = organisationLookup.ValueUnsafe();

        var detail = await GetDetailAsync(collection, organisation, cancellationToken);
        var hasSubmission = detail is not null;

        // 404 for an organisation that is neither assigned to this collection nor has a submission for it — prevents
        // probing arbitrary org ids, while still showing a now-unassigned org that nonetheless has real submission data
        // (it also appears in the status list). An assigned org with no submission still shows its derived status.
        if (!SubmissionEligibility.IsAssignedTo(collection, organisation) && !hasSubmission)
        {
            return Option<OrgSubmissionDetailDto>.None;
        }

        var (submittedByName, submittedByEmail) = await ResolveSubmitterAsync(detail?.SubmittedBy, cancellationToken);
        var submittedAt = detail?.SubmittedAt;
        var status = SubmissionStatusRules.Derive(hasSubmission, submittedAt, collection.SubmissionDeadline, utcNow);

        // An assigned organisation that has not opened its assessment still has the collection's sections to show, all
        // not started.
        var maturitySections = detail?.MaturitySections
            ?? (SubmissionEligibility.IsMaturityAssessment(collection)
                ? MaturityAssessmentControllerService.ToSectionSummaries(
                    MaturityCollectionForms.SectionCodesFor(collection),
                    new Dictionary<string, string>(StringComparer.Ordinal))
                : null);

        // Last, so every read above has finished: the same call DataCollectionsController.ExportOrgReport makes for
        // this collection's flow, and only a finalised report has a file to measure (BLUM-9018).
        long? exportSizeBytes = null;
        if (submittedAt is { } finalisedAt)
        {
            exportSizeBytes = await exportSizeService.MeasureAsync(
                new ReportExportSizeKey("dhsc", collectionId, organisationId, finalisedAt),
                () => SubmissionEligibility.IsMaturityAssessment(collection)
                    ? maturityExportService.GenerateForOrganisationAsync(collectionId, organisationId, cancellationToken)
                    : quarterlyExportService.GenerateForOrganisationAsync(collectionId, organisationId, cancellationToken));
        }

        return new OrgSubmissionDetailDto(
            collection.Id.Value,
            collection.Name,
            collection.GetStatus(utcNow).ToString(),
            collection.CollectionOpenDate,
            collection.SubmissionDeadline,
            collection.Quarter,
            collection.ReportingPeriodStart,
            collection.ReportingPeriodEnd,
            organisation.Id.Value,
            organisation.Name,
            status,
            hasSubmission,
            submittedByName,
            submittedByEmail,
            submittedAt,
            detail?.ReferenceNumber,
            collection.Type?.ToString(),
            collection.FinancialYear,
            maturitySections,
            detail?.MaturitySnapshot,
            exportSizeBytes);
    }

    // A window is reportable here when its type has a submission flow. Anything else (an unknown type) has nothing
    // to list, so the screens answer 404 rather than an empty table that looks like "nobody has started".
    private static bool HasSubmissionFlow(Collection collection) =>
        SubmissionEligibility.IsQuarterly(collection) || SubmissionEligibility.IsMaturityAssessment(collection);

    // Row presence = started; SubmittedAt = finalised. Keyed by organisation id for the assignment resolution.
    private async Task<Dictionary<Guid, DateTime?>> GetStartedRowsAsync(Collection collection, CancellationToken cancellationToken)
    {
        if (SubmissionEligibility.IsMaturityAssessment(collection))
        {
            var assessments = await maturityAssessmentQueries.GetStatusRowsForCollectionAsync(collection.Id, cancellationToken);
            return assessments.ToDictionary(a => a.OrganisationId, a => a.SubmittedAt);
        }

        var submissions = await submissionQueries.GetStatusRowsForCollectionAsync(collection.Id, cancellationToken);
        return submissions.ToDictionary(s => s.OrganisationId, s => s.SubmittedAt);
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
            var statusByCode = assessment.Sections.ToDictionary(s => s.SectionCode, s => s.Status, StringComparer.Ordinal);

            // The snapshot is only meaningful once frozen: an in-progress assessment's answers are the LA's to change.
            var snapshot = assessment.SubmittedAt is null
                ? null
                : MaturityAssessmentSnapshotJson.TryRead(assessment.SnapshotJson) ?? new MaturityAssessmentSnapshotDocument([]);

            return new SubmissionDetail(
                assessment.SubmittedAt,
                assessment.SubmittedBy,
                assessment.ReferenceNumber,
                MaturityAssessmentControllerService.ToSectionSummaries(
                    MaturityAssessmentSections.For(collection, assessment.SubmittedAt, assessment.SnapshotJson, assessment.Sections.Select(s => s.SectionCode)),
                    statusByCode),
                snapshot is null ? null : MaturityAssessmentControllerService.ToSnapshotDtos(snapshot));
        }

        var submissionLookup = await submissionQueries.GetDetailForCollectionAndOrgAsync(collection.Id, organisation.Id, cancellationToken);
        if (submissionLookup.IsNone)
        {
            return null;
        }

        var submission = submissionLookup.ValueUnsafe();
        return new SubmissionDetail(submission.SubmittedAt, submission.SubmittedBy, submission.ReferenceNumber, null, null);
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

    // The two flows' detail rows reduced to what the screen needs; the maturity members are null for a quarterly row.
    private sealed record SubmissionDetail(
        DateTime? SubmittedAt,
        Guid? SubmittedBy,
        string? ReferenceNumber,
        IReadOnlyList<MaturitySectionSummaryDto>? MaturitySections,
        IReadOnlyList<MaturitySnapshotSectionDto>? MaturitySnapshot);
}