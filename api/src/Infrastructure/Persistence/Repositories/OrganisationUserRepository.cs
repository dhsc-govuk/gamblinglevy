using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Domain.Organisations;
using Domain.OrganisationUsers;
using Domain.Users;
using Domain.ValueObjects;
using Infrastructure.Persistence.Extensions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class OrganisationUserRepository(ApplicationDbContext dbContext)
    : RepositoryBase<OrganisationUser, OrganisationUserId>(dbContext), IOrganisationUserRepository,
        IOrganisationUserQueries
{
    public async Task<IReadOnlyList<User>> GetActiveOrgAdminsAsync(OrganisationId organisationId, CancellationToken cancellationToken = default)
    {
        return await Context.OrganisationUsers
            .AsNoTracking()
            .Where(x => x.OrganisationId == organisationId)
            .Include(x => x.User)
            .Where(x => x.User!.Role == UserRole.OrganisationAdmin && x.User.Status == UserStatus.Active)
            .Select(x => x.User!)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<User>> GetActiveUsersAsync(OrganisationId organisationId, CancellationToken cancellationToken = default)
    {
        return await Context.OrganisationUsers
            .AsNoTracking()
            .Where(x => x.OrganisationId == organisationId)
            .Include(x => x.User)
            .Where(x => x.User!.Status == UserStatus.Active)
            .Select(x => x.User!)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OrganisationUserContact>> GetActiveUsersForOrganisationsAsync(
        IReadOnlyCollection<OrganisationId> organisationIds,
        CancellationToken cancellationToken = default)
    {
        if (organisationIds.Count == 0)
        {
            return [];
        }

        // Materialised as the strongly-typed ids rather than their Guid values: OrganisationId maps through
        // HasConversion, so Contains over the id records translates to the same IN (...) on organisation_id.
        var ids = organisationIds.Distinct().ToList();

        var rows = await Context.OrganisationUsers
            .AsNoTracking()
            .Where(x => ids.Contains(x.OrganisationId))
            .Include(x => x.User)
            .Where(x => x.User!.Status == UserStatus.Active)
            .Select(x => new { x.OrganisationId, UserId = x.User!.Id, x.User.Email, x.User.Name })
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => new OrganisationUserContact(row.OrganisationId.Value, row.UserId.Value, row.Email, row.Name.ToString()))
            .ToList();
    }

    public async Task RemoveByOrganisationAsync(OrganisationId organisationId, CancellationToken cancellationToken = default)
    {
        var rows = await Context.OrganisationUsers
            .Where(x => x.OrganisationId == organisationId)
            .ToListAsync(cancellationToken);

        if (rows.Count > 0)
        {
            Context.OrganisationUsers.RemoveRange(rows);
            await Context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task RemoveByUserIdAsync(UserId userId, CancellationToken cancellationToken = default)
    {
        var rows = await Context.OrganisationUsers
            .Where(x => x.UserId == userId)
            .ToListAsync(cancellationToken);

        if (rows.Count > 0)
        {
            Context.OrganisationUsers.RemoveRange(rows);
            await Context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<Option<OrganisationId>> FindOrganisationIdForUser(UserId userId, CancellationToken cancellationToken = default)
    {
        var orgId = await Context.OrganisationUsers
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => x.OrganisationId)
            .FirstOrDefaultAsync(cancellationToken);
        return orgId is null ? Option<OrganisationId>.None : orgId;
    }

    public async Task<Option<OrganisationUser>> FindByUserIdAsync(UserId userId, CancellationToken cancellationToken = default)
    {
        var entity = await Context.OrganisationUsers
            .AsNoTracking()
            .Include(x => x.Organisation)
            .FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        return entity ?? Option<OrganisationUser>.None;
    }

    public async Task<int> Count(Option<OrganisationId> id, Permission permission, CancellationToken cancellationToken)
    {
        var query = GetQuery(permission);
        return await id.MatchAsync(
            orgId => query.Where(x => x.OrganisationId == orgId).CountAsync(cancellationToken),
            () => query.CountAsync(cancellationToken));
    }

    public async Task<IReadOnlyList<OrganisationUser>> GetUsers(OrganisationId id, Permission permission, CancellationToken cancellationToken)
    {
        return await GetQuery(permission)
            .AsNoTracking()
            .Include(x => x.User)
            .Where(x => x.OrganisationId == id)
            .ToListAsync(cancellationToken);
    }

    public async Task<Option<OrganisationUser>> GetOrganisationUserById(OrganisationUserId id, Permission permission, CancellationToken cancellationToken = default)
    {
        var entity = await GetQuery(permission)
            .AsNoTracking()
            .Include(x => x.User)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        return entity ?? Option<OrganisationUser>.None;
    }

    // Lead reads its own LA's users; the mutating actions are gated to Role.UserManagers at the
    // controller, and AssignableRoles blocks Lead as an actor regardless (BLUM-7038 AC3).
    private IQueryable<OrganisationUser> GetQuery(Permission permission) =>
        Context.OrganisationUsers.ScopedTo(permission, orgId => x => x.OrganisationId == orgId);
}