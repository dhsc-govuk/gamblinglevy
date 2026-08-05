using Domain.Activities;
using Domain.Organisations;
using LanguageExt;

namespace Application.Common.Interfaces.Repositories;

public interface IActivityRepository
{
    Task<Activity> AddAsync(Activity entity, CancellationToken cancellationToken = default);
    Task<Activity> UpdateAsync(Activity entity, CancellationToken cancellationToken = default);

    // Organisation-scoped load used by mutation handlers before applying domain changes. By default excludes
    // soft-deleted records; pass includeDeleted to load a deleted record (e.g. for restore).
    Task<Option<Activity>> GetByIdForOrgAsync(ActivityId id, OrganisationId organisationId, bool includeDeleted = false, CancellationToken cancellationToken = default);
}