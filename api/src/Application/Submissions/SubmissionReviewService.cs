using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Domain.Organisations;
using LanguageExt.UnsafeValueAccess;
using Microsoft.Extensions.Logging;

namespace Application.Submissions;

// Flags the organisation's in-flight quarterly-report submissions for review when its activity list changes
// (create / update / delete / restore) after the list was confirmed — the LA must revisit every started section
// (Figma: "Your list of activities has changed. You must review and reconfirm each section…"). Coarse-grained by
// design: any post-confirmation list change counts. Called by the activity command handlers after their own save;
// a failure here must never fail the activity operation, so everything is swallowed and logged.
public class SubmissionReviewService(
    ICollectionQueries collectionQueries,
    ISubmissionQueries submissionQueries,
    ISubmissionRepository submissionRepository,
    IOrganisationQueries organisationQueries,
    ILogger<SubmissionReviewService> logger)
{
    public async Task FlagOpenSubmissionsAsync(OrganisationId organisationId, CancellationToken cancellationToken)
    {
        try
        {
            // Resolve the org so IsAssignedTo can apply the type-aware gate (GetAssignedToOrganisationAsync is a
            // coarse prefilter). An all-LAs collection is only assigned to Local-Authority orgs.
            var organisationLookup = await organisationQueries.GetOrganisationById(organisationId, cancellationToken);
            if (organisationLookup.IsNone)
            {
                return;
            }

            var organisation = organisationLookup.ValueUnsafe();
            var collections = await collectionQueries.GetAssignedToOrganisationAsync(organisationId, cancellationToken);

            foreach (var collection in collections.Where(c =>
                SubmissionEligibility.IsQuarterly(c)
                && SubmissionEligibility.IsOpen(c)
                && SubmissionEligibility.IsAssignedTo(c, organisation)))
            {
                var submissionLookup = await submissionQueries.GetByCollectionAndOrgAsync(collection.Id, organisationId, cancellationToken);
                if (submissionLookup.IsNone)
                {
                    continue;
                }

                var submission = submissionLookup.ValueUnsafe();
                if (!submission.AreActivitiesConfirmed)
                {
                    continue;
                }

                submission.FlagActivityListChanged();
                await submissionRepository.UpdateAsync(submission, cancellationToken);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to flag open submissions for review after an activity-list change for organisation {OrganisationId}.",
                organisationId);
        }
    }
}