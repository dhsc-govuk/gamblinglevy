using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Domain.Locations;
using Domain.Organisations;
using Infrastructure.Persistence.Extensions;
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
        return await GetQuery(permission)
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
            orgId => GetQuery(permission)
                .CountAsync(x => x.OrganisationId == orgId, cancellationToken),
            () => GetQuery(permission)
                .CountAsync(cancellationToken));
    }

    private IQueryable<Location> GetLocationByIdQuery(LocationId id, Permission permission)
    {
        return GetQuery(permission).Where(x => x.Id == id);
    }

    // Every LA role sees its own organisation's locations; ScopedTo throws for a role it cannot scope.
    private IQueryable<Location> GetQuery(Permission permission) =>
        Context.Locations.AsNoTracking().ScopedTo(permission, orgId => x => x.OrganisationId == orgId);
}