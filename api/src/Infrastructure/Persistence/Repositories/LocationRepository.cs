using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Domain.Locations;
using Domain.Organisations;
using Domain.ValueObjects;
using LanguageExt;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;
public class LocationRepository(ApplicationDbContext dbContext)
    : RepositoryBase<Location, LocationId>(dbContext), ILocationRepository, ILocationQueries
{
    public async Task<IReadOnlyList<Location>> GetAll(
        OrganisationId id,
        Permission permission,
        CancellationToken cancellationToken)
    {
        return await GetQuery(permission.UserRole, permission.OrganisationId)
            .Where(x => x.OrganisationId == id)
            .ToListAsync(cancellationToken);
    }

    public async Task<Option<Location>> GetById(LocationId id, Permission permission, CancellationToken cancellationToken)
    {
        var entity = await GetLocationByIdQuery(id, permission)
            .AsNoTracking()
            .Include(x => x.Organisation)
            .FirstOrDefaultAsync(cancellationToken);
        return entity ?? Option<Location>.None;
    }

    public async Task<Option<Location>> FindByNameAsync(string name, OrganisationId organisationId, CancellationToken cancellationToken)
    {
        var entity = await Context.Locations.FirstOrDefaultAsync(x => x.Name == name && x.OrganisationId == organisationId, cancellationToken);
        return entity ?? Option<Location>.None;
    }

    public async Task<Option<Location>> FindDuplicateAsync(string targetName, OrganisationId organisationId, LocationId sourceId, CancellationToken cancellationToken = default)
    {
        var entity = await Context.Locations
            .FirstOrDefaultAsync(x => x.Name == targetName && x.OrganisationId == organisationId && x.Id != sourceId, cancellationToken);
        return entity ?? Option<Location>.None;
    }

    public async Task<int> Count(
        Option<OrganisationId> organisationId,
        Permission permission,
        CancellationToken cancellationToken)
    {
        return await organisationId.MatchAsync(
            orgId => GetQuery(permission.UserRole, permission.OrganisationId)
                .CountAsync(x => x.OrganisationId == orgId, cancellationToken),
            () => GetQuery(permission.UserRole, permission.OrganisationId)
                .CountAsync(cancellationToken));
    }

    private IQueryable<Location> GetLocationByIdQuery(LocationId id, Permission permission)
    {
        return GetQuery(permission.UserRole, permission.OrganisationId).Where(x => x.Id == id);
    }

    private IQueryable<Location> GetQuery(UserRole userRole, Option<OrganisationId> organisationId)
    {
        if (userRole.Equals(UserRole.Admin))
        {
            return Context.Locations.AsNoTracking();
        }

        // Every LA role sees its own organisation's locations. A role missing from every branch here
        // falls through to an empty queryable rather than an error, so it reads as "no locations" —
        // which is how Lead's broken access stayed invisible (BLUM-7038).
        if (userRole.IsOrganisationMember())
        {
            return organisationId.Match(
                orgId => Context.Locations.AsNoTracking().Where(x => x.OrganisationId == orgId),
                Array.Empty<Location>().AsQueryable());
        }

        return Array.Empty<Location>().AsQueryable();
    }
}