using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Domain.AuditLogs;
using Domain.Users;
using Domain.ValueObjects;
using LanguageExt;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class UserRepository(ApplicationDbContext dbContext)
    : RepositoryBase<User, UserId>(dbContext), IUserRepository, IUserQueries
{
    public async Task<int> Count(CancellationToken cancellationToken = default)
    {
        return await Context.Users.AsQueryable().Where(x => x.Role == UserRole.Admin).CountAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<User>> GetAdminUsers(CancellationToken cancellationToken = default)
    {
        return await Context.Users.AsQueryable().Where(x => x.Role == UserRole.Admin).AsNoTracking().ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<UserListItem>> GetAllUsers(CancellationToken cancellationToken = default)
    {
        var users = await Context.Users.AsNoTracking().ToListAsync(cancellationToken);

        var orgUsers = await Context.OrganisationUsers
            .Include(x => x.Organisation)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var orgByUserId = orgUsers.ToDictionary(x => x.UserId, x => x.Organisation?.Name);

        return users
            .Select(u => new UserListItem(
                u.Id.Value,
                u.Name.FirstName,
                u.Name.LastName,
                u.Email,
                u.Status.ToString(),
                u.Role.ToString(),
                orgByUserId.TryGetValue(u.Id, out var orgName) ? orgName : null))
            .ToList();
    }

    public async Task<IReadOnlyList<User>> GetActiveAdminsAsync(CancellationToken cancellationToken = default)
    {
        return await Context.Users
            .AsQueryable()
            .Where(x => x.Role == UserRole.Admin && x.Status == UserStatus.Active)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<Option<User>> FindById(UserId id, CancellationToken cancellationToken = default)
    {
        var user = await Context.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        return user ?? Option<User>.None;
    }

    public async Task<Option<UserDetailItem>> FindByIdWithDetails(UserId id, CancellationToken cancellationToken = default)
    {
        var user = await Context.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (user == null)
        {
            return Option<UserDetailItem>.None;
        }

        var orgUser = await Context.OrganisationUsers
            .Include(x => x.Organisation)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == id, cancellationToken);

        var mfa = await Context.UserMfas
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == id, cancellationToken);

        var createdAudit = await Context.AuditLogs
            .AsNoTracking()
            .Where(x => x.TargetType == AuditTargetType.User && x.TargetId == id.Value && x.Action == AuditAction.UserCreated)
            .OrderBy(x => x.OccurredAt)
            .FirstOrDefaultAsync(cancellationToken);

        var updatedAudit = await Context.AuditLogs
            .AsNoTracking()
            .Where(x => x.TargetType == AuditTargetType.User && x.TargetId == id.Value && x.Action == AuditAction.UserUpdated)
            .OrderByDescending(x => x.OccurredAt)
            .FirstOrDefaultAsync(cancellationToken);

        var deletedAudit = await Context.AuditLogs
            .AsNoTracking()
            .Where(x => x.TargetType == AuditTargetType.User && x.TargetId == id.Value && x.Action == AuditAction.UserDeleted)
            .OrderByDescending(x => x.OccurredAt)
            .FirstOrDefaultAsync(cancellationToken);

        var actorGuids = new List<Guid>();
        if (createdAudit != null)
        {
            actorGuids.Add(createdAudit.ActorUserId.Value);
        }

        if (updatedAudit != null)
        {
            actorGuids.Add(updatedAudit.ActorUserId.Value);
        }

        if (deletedAudit != null)
        {
            actorGuids.Add(deletedAudit.ActorUserId.Value);
        }

        var actorUserIds = actorGuids.Select(g => new UserId(g)).ToList();
        var actorNames = actorUserIds.Count > 0
            ? await Context.Users
                .AsNoTracking()
                .Where(x => actorUserIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id.Value, x => $"{x.Name.FirstName} {x.Name.LastName}", cancellationToken)
            : new Dictionary<Guid, string>();

        UserHistoryEntry? GetHistoryEntry(AuditLog? audit)
        {
            if (audit == null)
            {
                return null;
            }

            var name = actorNames.TryGetValue(audit.ActorUserId.Value, out var n) ? n : "Unknown";
            return new UserHistoryEntry(name, audit.OccurredAt);
        }

        return new UserDetailItem(
            user.Id.Value,
            user.Name.FirstName,
            user.Name.LastName,
            user.Email,
            user.Status.ToString(),
            user.Role.ToString(),
            orgUser?.Organisation?.Name,
            orgUser?.Organisation?.Id.Value,
            mfa?.IsEnabled ?? false,
            GetHistoryEntry(createdAudit),
            GetHistoryEntry(updatedAudit),
            GetHistoryEntry(deletedAudit));
    }

    public async Task<Option<User>> FindByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        var entity = await Context.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Email == email, cancellationToken);
        return entity ?? Option<User>.None;
    }

    public async Task<Option<User>> FindDuplicateAsync(
        string targetEmail,
        UserId sourceId,
        CancellationToken cancellationToken = default)
    {
        var entity = await Context.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Email == targetEmail && x.Id != sourceId, cancellationToken);
        return entity ?? Option<User>.None;
    }
}