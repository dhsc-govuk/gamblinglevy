using Application.Common.Interfaces;
using Domain.Users;
using Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

/// <summary>
/// Backs <c>ResetTestDataCommand</c>. Truncates every table the EF model knows about except <c>users</c>,
/// <c>user_mfas</c> and <c>mfa_sessions</c> — which is exactly the application's tables, since
/// <c>__EFMigrationsHistory</c> is never a mapped entity — then deletes every non-admin's row from those
/// three tables and re-runs the existing idempotent seed protocol. Truncating everything and reseeding,
/// rather than trying to tell a seeded row apart from an admin-added one, is deliberate: Forms and
/// DataCollectionForms each hold both in the same table, so no row-level flag exists to key a selective
/// delete on. The three carve-outs are all one carve-out: DHSC admin accounts are real people's platform
/// access, not test fixtures, so this reset must leave an admin exactly as it found them — not just their
/// <c>users</c> row, but their TOTP enrolment and live sessions too. Missing user_mfas/mfa_sessions was a bug
/// here, not a smaller version of the same carve-out: every admin's authenticator was wiped on every reset,
/// forcing re-enrolment, because those two tables were being truncated unconditionally.
/// </summary>
public class DataResetService(ApplicationDbContext dbContext, ApplicationDbContextInitialiser initialiser)
    : IDataResetService
{
    public async Task ResetToSeedDataAsync(CancellationToken cancellationToken)
    {
        try
        {
            await TruncateAndReseedAsync(cancellationToken);
        }
        catch
        {
            // A reset can fail after the truncate has committed, half-way through the reseed. ResetTestData then
            // audits the failure, and that save must not also flush the half-seeded rows still tracked here.
            dbContext.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task TruncateAndReseedAsync(CancellationToken cancellationToken)
    {
        var preservedTableNames = new[]
        {
            dbContext.Model.FindEntityType(typeof(User))?.GetTableName(),
            dbContext.Model.FindEntityType(typeof(UserMfa))?.GetTableName(),
            dbContext.Model.FindEntityType(typeof(MfaSession))?.GetTableName(),
        };

        var tableNames = dbContext.Model.GetEntityTypes()
            .Select(entityType => entityType.GetTableName())
            .Where(name => name is not null && !preservedTableNames.Contains(name))
            .Distinct()
            .Select(name => $"\"{name}\"")
            .ToList();

        if (tableNames.Count > 0)
        {
            // Every other table is a child of users (an actor/creator FK) or of one of those children, so
            // CASCADE empties them without ever needing to touch users, user_mfas or mfa_sessions — which is
            // what leaves the deletes below with nothing still referencing the rows they remove.
            var truncateSql = $"TRUNCATE TABLE {string.Join(", ", tableNames)} RESTART IDENTITY CASCADE;";
            await dbContext.Database.ExecuteSqlRawAsync(truncateSql, cancellationToken);
        }

        var adminIds = await dbContext.Users
            .Where(user => user.Role == UserRole.Admin)
            .Select(user => user.Id)
            .ToListAsync(cancellationToken);

        // Order matters: a non-admin's MFA enrolment and sessions have to go before the user row itself, or
        // the delete below leaves them referencing a user that no longer exists.
        await dbContext.UserMfas
            .Where(mfa => !adminIds.Contains(mfa.UserId))
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.MfaSessions
            .Where(session => !adminIds.Contains(session.UserId))
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.Users
            .Where(user => user.Role != UserRole.Admin)
            .ExecuteDeleteAsync(cancellationToken);

        // The truncate and deletes above can leave stale tracked entities from earlier in this scope's
        // lifetime; SeedDataAsync's SaveChangesAsync calls must start from a clean tracker or they can try to
        // update rows that no longer exist.
        dbContext.ChangeTracker.Clear();

        await initialiser.SeedDataAsync();
    }

    public async Task<MfaDataCleared> ClearMfaDataAsync(CancellationToken cancellationToken)
    {
        // No admin carve-out here, deliberately unlike ResetToSeedDataAsync above: this exists for the
        // Data Protection key-persistence cutover, where every already-encrypted secret — an admin's
        // included — becomes undecryptable at once. UserMfas first, same order as the reset above, though
        // nothing here actually depends on it: both tables key off UserId independently.
        // One transaction, so the audit row that follows describes what actually happened: either both tables were
        // cleared or neither was, never every enrolment gone with the sessions left behind (BLUM-8747). The request
        // token only governs starting; once the first delete runs, a dropped caller must not abort it half-way.
        // ClearMfaDataCommand runs this inside its ITransactionRunner so the audit row shares the transaction
        // (BLUM-8748); that one is joined and owns the commit. Run on its own, this still opens its own.
        await using var ownTransaction = dbContext.Database.CurrentTransaction is null
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        var userMfasDeleted = await dbContext.UserMfas.ExecuteDeleteAsync(CancellationToken.None);
        var sessionsDeleted = await dbContext.MfaSessions.ExecuteDeleteAsync(CancellationToken.None);
        if (ownTransaction is not null)
        {
            await ownTransaction.CommitAsync(CancellationToken.None);
        }

        return new MfaDataCleared(userMfasDeleted, sessionsDeleted);
    }

    public async Task<UserStatistics> GetUserStatisticsAsync(CancellationToken cancellationToken)
    {
        var totalUsers = await dbContext.Users.CountAsync(cancellationToken);
        var activeUsers = await dbContext.Users.CountAsync(user => user.Status == UserStatus.Active, cancellationToken);

        var usersLoggedInAtLeastOnce = await dbContext.MfaSessions
            .Select(session => session.UserId)
            .Distinct()
            .CountAsync(cancellationToken);

        var enabledMfaUserIds = await dbContext.UserMfas
            .Where(mfa => mfa.IsEnabled)
            .Select(mfa => mfa.UserId)
            .ToListAsync(cancellationToken);

        var usersPendingMfaEnrolmentEmails = await dbContext.Users
            .Where(user => !enabledMfaUserIds.Contains(user.Id))
            .Select(user => user.Email)
            .ToListAsync(cancellationToken);

        return new UserStatistics(
            TotalUsers: totalUsers,
            ActiveUsers: activeUsers,
            DeactivatedUsers: totalUsers - activeUsers,
            UsersLoggedInAtLeastOnce: usersLoggedInAtLeastOnce,
            UsersPendingMfaEnrolment: usersPendingMfaEnrolmentEmails.Count,
            UsersPendingMfaEnrolmentEmails: usersPendingMfaEnrolmentEmails);
    }
}