using Domain.Activities;
using Domain.Organisations;
using LanguageExt;

namespace Application.Common.Interfaces.Queries;

public interface IActivityQueries
{
    // All non-deleted records for the organisation (drafts included), optionally filtered by status. The list
    // screen groups these by status client-side. A status of "draft" returns incomplete (setup-unfinished) records.
    Task<IReadOnlyList<Activity>> GetByOrganisationAsync(OrganisationId organisationId, string? status, CancellationToken cancellationToken = default);

    // Completed, non-deleted Active records — feeds the review screen and the future quarterly-return generation.
    Task<IReadOnlyList<Activity>> GetActiveByOrganisationAsync(OrganisationId organisationId, CancellationToken cancellationToken = default);

    // Soft-deleted records for the organisation — feeds the "Deleted activities" page.
    Task<IReadOnlyList<Activity>> GetDeletedByOrganisationAsync(OrganisationId organisationId, CancellationToken cancellationToken = default);

    Task<Option<Activity>> GetByIdForOrgAsync(ActivityId id, OrganisationId organisationId, bool includeDeleted = false, CancellationToken cancellationToken = default);

    // Batched reads for the maturity-assessment pre-fill (BLUM-7856), which walks every reportable activity of an
    // organisation at once — the per-activity repositories would be an N+1 over ~50 records. AsNoTracking.
    Task<IReadOnlyList<ActivityAnswer>> GetAnswersForActivitiesAsync(IReadOnlyCollection<ActivityId> activityIds, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ActivityExpectedResult>> GetExpectedResultsForActivitiesAsync(IReadOnlyCollection<ActivityId> activityIds, CancellationToken cancellationToken = default);
}