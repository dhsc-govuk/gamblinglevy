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
}