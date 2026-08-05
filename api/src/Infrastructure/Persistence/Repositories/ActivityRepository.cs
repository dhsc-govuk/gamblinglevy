using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Domain.Activities;
using Domain.Organisations;
using LanguageExt;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class ActivityRepository(ApplicationDbContext dbContext)
    : RepositoryBase<Activity, ActivityId>(dbContext), IActivityRepository, IActivityQueries
{
    public async Task<Option<Activity>> GetByIdForOrgAsync(
        ActivityId id,
        OrganisationId organisationId,
        bool includeDeleted = false,
        CancellationToken cancellationToken = default)
    {
        var query = Context.Activities
            .AsNoTracking()
            .Where(x => x.Id == id && x.OrganisationId == organisationId);

        if (!includeDeleted)
        {
            query = query.Where(x => x.DeletedAt == null);
        }

        var entity = await query.FirstOrDefaultAsync(cancellationToken);
        return entity ?? Option<Activity>.None;
    }

    public async Task<IReadOnlyList<Activity>> GetByOrganisationAsync(
        OrganisationId organisationId,
        string? status,
        CancellationToken cancellationToken = default)
    {
        var query = Context.Activities
            .AsNoTracking()
            .Where(x => x.OrganisationId == organisationId && x.DeletedAt == null);

        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalised = status.Trim().ToLowerInvariant();
            if (normalised == ActivityStatuses.DraftFilter)
            {
                query = query.Where(x => !x.IsComplete);
            }
            else if (ActivityStatus.IsSupported(normalised))
            {
                var parsed = ActivityStatus.From(normalised);
                query = query.Where(x => x.IsComplete && x.Status == parsed);
            }
            else
            {
                // Unknown status filter matches nothing — avoids throwing on an unvalidated query parameter.
                return [];
            }
        }

        return await query.OrderByDescending(x => x.CreatedAt).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Activity>> GetActiveByOrganisationAsync(
        OrganisationId organisationId,
        CancellationToken cancellationToken = default)
    {
        var active = ActivityStatus.Active;
        return await Context.Activities
            .AsNoTracking()
            .Where(x => x.OrganisationId == organisationId && x.DeletedAt == null && x.IsComplete && x.Status == active)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Activity>> GetDeletedByOrganisationAsync(
        OrganisationId organisationId,
        CancellationToken cancellationToken = default)
    {
        return await Context.Activities
            .AsNoTracking()
            .Where(x => x.OrganisationId == organisationId && x.DeletedAt != null)
            .OrderByDescending(x => x.DeletedAt)
            .ToListAsync(cancellationToken);
    }
}