using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Domain.Locations;
using Domain.LocationUsers;
using Domain.Organisations;
using Domain.Users;
using Domain.ValueObjects;
using LanguageExt;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class LocationUserRepository(ApplicationDbContext dbContext)
    : RepositoryBase<LocationUser, LocationUserId>(dbContext), ILocationUserRepository, ILocationUserQueries
{
    public async Task<IReadOnlyList<LocationUser>> GetAll(
        LocationId id,
        Permission permission,
        CancellationToken cancellationToken)
    {
        return await GetQuery(permission.UserRole, permission.OrganisationId)
            .Where(x => x.LocationId == id)
            .ToListAsync(cancellationToken);
    }

    public async Task<Option<LocationUser>> GetById(
        LocationUserId id,
        Permission permission,
        CancellationToken cancellationToken)
    {
        var entity = await GetQuery(permission.UserRole, permission.OrganisationId)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        return entity ?? Option<LocationUser>.None;
    }

    public async Task RemoveByUserIdAsync(UserId userId, CancellationToken cancellationToken = default)
    {
        var rows = await Context.LocationUsers
            .Where(x => x.UserId == userId)
            .ToListAsync(cancellationToken);

        if (rows.Count > 0)
        {
            Context.LocationUsers.RemoveRange(rows);
            await Context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<int> Count(
        Option<LocationId> locationId,
        Permission permission,
        CancellationToken cancellationToken)
    {
        var query = GetQuery(permission.UserRole, permission.OrganisationId);

        return await locationId.MatchAsync(
            l => query.CountAsync(x => x.LocationId == l, cancellationToken),
            () => query.CountAsync(cancellationToken));
    }

    private IQueryable<LocationUser> GetQuery(UserRole userRole, Option<OrganisationId> organisationId)
    {
        var query = Context.LocationUsers
            .Include(x => x.User)
            .Include(x => x.Location)
            .AsNoTracking()
            .AsQueryable();

        if (userRole.Equals(UserRole.Admin))
        {
            return query;
        }

        if (userRole.IsOrganisationMember())
        {
            return organisationId.Match(
                orgId => query.Where(x => x.Location!.OrganisationId == orgId),
                Array.Empty<LocationUser>().AsQueryable());
        }

        return Array.Empty<LocationUser>().AsQueryable();
    }
}