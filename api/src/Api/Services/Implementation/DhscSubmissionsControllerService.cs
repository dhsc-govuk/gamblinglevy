using Api.Dtos;
using Api.Services.Abstract;
using Application.Common.Interfaces.Queries;
using Application.Submissions;
using Domain.Collections;
using Domain.Organisations;
using Domain.Users;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;

namespace Api.Services.Implementation;

// Neither screen takes a dependency on the export service: both used to measure a downloadable ZIP by building it,
// which is what made a read expensive enough to matter and gave a read a way to fail. The download endpoints own
// the export outright now.
public class DhscSubmissionsControllerService(
    ICollectionQueries collectionQueries,
    ISubmissionQueries submissionQueries,
    IOrganisationQueries organisationQueries,
    IUserQueries userQueries) : IDhscSubmissionsControllerService
{
    public async Task<Option<CollectionSubmissionSummaryDto>> GetOrgStatusesAsync(
        Guid collectionId,
        CancellationToken cancellationToken)
    {
        var lookup = await collectionQueries.GetByIdAsync(new CollectionId(collectionId), includeDeleted: false, cancellationToken);
        if (lookup.IsNone)
        {
            return Option<CollectionSubmissionSummaryDto>.None;
        }

        var collection = lookup.ValueUnsafe();

        // The DHSC submission-status screens are a quarterly-report concept; maturity assessments have no submission
        // flow, so a non-quarterly window is not reportable here (mirrors QuarterlyReportExportService).
        if (!SubmissionEligibility.IsQuarterly(collection))
        {
            return Option<CollectionSubmissionSummaryDto>.None;
        }

        var allOrganisations = await organisationQueries.GetOrganisations(cancellationToken);
        var submissions = await submissionQueries.GetStatusRowsForCollectionAsync(collection.Id, cancellationToken);
        var submissionByOrg = submissions.ToDictionary(s => s.OrganisationId);

        // Include every assigned org plus any org that has a submission row even if no longer assigned, so its data is
        // never silently dropped from the status list.
        var reportable = SubmissionAssignment.ResolveReportableOrganisations(collection, allOrganisations, submissionByOrg.Keys);

        var organisations = reportable
            .OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
            .Select(o =>
            {
                var hasSubmission = submissionByOrg.TryGetValue(o.Id.Value, out var row);
                var submittedAt = hasSubmission ? row.SubmittedAt : null;
                var status = SubmissionStatusRules.Derive(hasSubmission, submittedAt, collection.SubmissionDeadline);

                return new OrgSubmissionStatusDto(o.Id.Value, o.Name, status, submittedAt);
            })
            .ToList();

        return new CollectionSubmissionSummaryDto(
            collection.Id.Value,
            collection.Name,
            collection.GetStatus().ToString(),
            collection.CollectionOpenDate,
            collection.SubmissionDeadline,
            collection.Quarter,
            collection.ReportingPeriodStart,
            collection.ReportingPeriodEnd,
            organisations);
    }

    public async Task<Option<OrgSubmissionDetailDto>> GetOrgDetailAsync(
        Guid collectionId,
        Guid organisationId,
        CancellationToken cancellationToken)
    {
        var lookup = await collectionQueries.GetByIdAsync(new CollectionId(collectionId), includeDeleted: false, cancellationToken);
        if (lookup.IsNone)
        {
            return Option<OrgSubmissionDetailDto>.None;
        }

        var collection = lookup.ValueUnsafe();

        // Quarterly-only: a maturity-assessment window has no submission detail to show.
        if (!SubmissionEligibility.IsQuarterly(collection))
        {
            return Option<OrgSubmissionDetailDto>.None;
        }

        var organisationLookup = await organisationQueries.GetOrganisationById(new OrganisationId(organisationId), cancellationToken);
        if (organisationLookup.IsNone)
        {
            return Option<OrgSubmissionDetailDto>.None;
        }

        var organisation = organisationLookup.ValueUnsafe();

        var detailLookup = await submissionQueries.GetDetailForCollectionAndOrgAsync(collection.Id, organisation.Id, cancellationToken);
        var hasSubmission = detailLookup.IsSome;

        // 404 for an organisation that is neither assigned to this collection nor has a submission for it — prevents
        // probing arbitrary org ids, while still showing a now-unassigned org that nonetheless has real submission data
        // (it also appears in the status list). An assigned org with no submission still shows its derived status.
        if (!SubmissionEligibility.IsAssignedTo(collection, organisation) && !hasSubmission)
        {
            return Option<OrgSubmissionDetailDto>.None;
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

        return new OrgSubmissionDetailDto(
            collection.Id.Value,
            collection.Name,
            collection.GetStatus().ToString(),
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
            hasSubmission ? detail.ReferenceNumber : null);
    }
}