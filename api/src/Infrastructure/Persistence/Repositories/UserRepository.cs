using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Domain.AuditLogs;
using Domain.Users;
using Domain.ValueObjects;
using Infrastructure.Persistence.Extensions;
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

    public async Task<IReadOnlyList<User>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await Context.Users.AsNoTracking().ToListAsync(cancellationToken);
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

    public async Task<Option<User>> FindInScope(UserId id, Permission permission, CancellationToken cancellationToken = default)
    {
        var user = await Context.Users
            .AsNoTracking()
            .ScopedTo(permission, orgId => u => Context.OrganisationUsers.Any(m => m.UserId == u.Id && m.OrganisationId == orgId))
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
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
            user.PasswordLoginEnabled,
            GetHistoryEntry(createdAudit),
            GetHistoryEntry(updatedAudit),
            GetHistoryEntry(deletedAudit));
    }

    public async Task<Option<User>> FindByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        // Case-insensitive on purpose. Email addresses are not case-sensitive in practice, and Microsoft Graph's
        // `mail eq '...'` filter is not either — so an ordinal comparison here lets "J.Smith@x" and "j.smith@x"
        // both miss this guard, both receive the *same* subId back from Graph, and both write a users row.
        // Nothing constrains that: neither users.email nor users.sub_id has a unique index, and
        // UserProfileRepository.GetUserProfile resolves the profile with SingleOrDefaultAsync on sub_id, so
        // from that point every request either account makes throws — an unrecoverable 500 at sign-in for two
        // real people, fixable only in the database. Reachable from any create path; the CSV importer just
        // makes a hand-maintained 153-row spreadsheet the likely way in.
        //
        // ToLower rather than EF.Functions.ILike: '_' is legal in a local part and ILike would treat it as a
        // single-character wildcard.
        var normalised = email.ToLower();
        var entity = await Context.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Email.ToLower() == normalised, cancellationToken);
        return entity ?? Option<User>.None;
    }

    public async Task<Option<User>> FindDuplicateAsync(
        string targetEmail,
        UserId sourceId,
        CancellationToken cancellationToken = default)
    {
        // Case-insensitive for the same reason as FindByEmailAsync above — an ordinal comparison here
        // would let an update collide with an existing row that differs only by case.
        var normalised = targetEmail.ToLower();
        var entity = await Context.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Email.ToLower() == normalised && x.Id != sourceId, cancellationToken);
        return entity ?? Option<User>.None;
    }
}