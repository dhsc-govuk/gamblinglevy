using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.Common.Settings;
using Application.Mfa.Dtos;
using Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class MfaSessionRepository(ApplicationDbContext dbContext, ApplicationSettings settings)
    : IMfaSessionRepository, IMfaSessionQueries
{
    public async Task<MfaSession?> GetByIdAsync(MfaSessionId sessionId, CancellationToken cancellationToken = default)
    {
        return await dbContext.MfaSessions
            .FirstOrDefaultAsync(x => x.Id == sessionId, cancellationToken);
    }

    public async Task<List<MfaSession>> GetByUserIdAsync(UserId userId, CancellationToken cancellationToken = default)
    {
        return await dbContext.MfaSessions
            .Where(x => x.UserId == userId && !x.IsRevoked)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<MfaSession>> GetActiveByUserIdAsync(UserId userId, CancellationToken cancellationToken = default)
    {
        return await ActiveSessionsQuery(userId, DateTime.UtcNow)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MfaSessionListItemDto>> GetActiveForUserAsync(UserId userId, CancellationToken cancellationToken = default)
    {
        return await ActiveSessionsQuery(userId, DateTime.UtcNow)
            .AsNoTracking()
            .Select(x => new MfaSessionListItemDto(
                x.Id.Value,
                x.DeviceLabel,
                x.IpAddress,
                x.CreatedAt,
                x.LastActivityAt,
                x.ExpiresAt,
                false))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// The single definition of "this session is still live", shared by the read side and by the concurrency
    /// policy so the list a user is shown and the set the cap is enforced against can never disagree. The
    /// comparisons mirror MfaSession.Evaluate exactly — it treats a session as timed out only once the window
    /// has been passed, so the boundary is inclusive here.
    /// </summary>
    private IOrderedQueryable<MfaSession> ActiveSessionsQuery(UserId userId, DateTime utcNow)
    {
        var idleCutoff = utcNow.AddMinutes(-settings.Mfa.IdleTimeoutMinutes);

        return dbContext.MfaSessions
            .Where(x => x.UserId == userId
                        && !x.IsRevoked
                        && x.ExpiresAt >= utcNow
                        && x.LastActivityAt >= idleCutoff)
            .OrderByDescending(x => x.LastActivityAt);
    }

    public async Task<MfaSession> AddAsync(MfaSession session, CancellationToken cancellationToken = default)
    {
        await dbContext.MfaSessions.AddAsync(session, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return session;
    }

    public async Task<MfaSession> UpdateAsync(MfaSession session, CancellationToken cancellationToken = default)
    {
        dbContext.MfaSessions.Update(session);
        await dbContext.SaveChangesAsync(cancellationToken);
        return session;
    }

    public async Task<int> TryRevokeTimedOutSessionAsync(MfaSessionId sessionId, MfaSessionEndReason reason, CancellationToken cancellationToken = default)
    {
        // Conditional on the row still being live, so that when several requests observe the same timeout at
        // once exactly one of them changes anything — and therefore exactly one writes the audit entry.
        return await dbContext.MfaSessions
            .Where(x => x.Id == sessionId && !x.IsRevoked)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.IsRevoked, true)
                    .SetProperty(x => x.EndedReason, reason),
                cancellationToken);
    }

    public async Task<int> TryRevokeSessionAsync(MfaSessionId sessionId, CancellationToken cancellationToken = default)
    {
        // Conditional on the row still being live, for the same reason TryRevokeTimedOutSessionAsync is: when
        // two sign-ins race to evict the same surplus session, exactly one of them changes anything and so
        // exactly one writes the audit entry. It also cannot throw a concurrency exception the way loading
        // and saving the entity can, which matters on the sign-in path.
        return await dbContext.MfaSessions
            .Where(x => x.Id == sessionId && !x.IsRevoked)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(x => x.IsRevoked, true),
                cancellationToken);
    }

    public async Task<int> TryUpdateLastActivityAsync(MfaSessionId sessionId, DateTime lastActivityAt, CancellationToken cancellationToken = default)
    {
        // Only last_activity_at is written, and only while the session is live, so a slide racing a
        // revocation can neither resurrect the session nor undo any other column.
        return await dbContext.MfaSessions
            .Where(x => x.Id == sessionId && !x.IsRevoked)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(x => x.LastActivityAt, lastActivityAt),
                cancellationToken);
    }

    public async Task<SessionDeletionResult> DeleteEndedSessionsAsync(DateTime endedBefore, int batchSize, CancellationToken cancellationToken = default)
    {
        // ExpiresAt alone is the whole test, and deliberately so.
        //
        // It is bounded — always CreatedAt + SessionExpiryHours — so a row whose ExpiresAt is already past the
        // cutoff belongs to a session that could not have carried a request since then, whatever its
        // IsRevoked flag says. Adding "or IsRevoked" would delete a session revoked seconds ago, and that row
        // is exactly what MfaRequiredMiddleware needs to keep answering SESSION_REVOKED rather than
        // MFA_REQUIRED — which would send the revoked browser to re-enter a code and undo the revocation.
        //
        // Two statements rather than one because ExecuteDelete does not accept Take(). The select is served by
        // ix_mfa_sessions_expires_at, so it is an index range scan over exactly the doomed rows rather than a
        // table scan, and the bound keeps the first run after a deployment — which may face a long backlog —
        // from turning into one enormous delete holding locks on a shared table.
        var doomed = await dbContext.MfaSessions
            .Where(x => x.ExpiresAt < endedBefore)
            .OrderBy(x => x.ExpiresAt)
            .Take(batchSize)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        if (doomed.Count == 0)
        {
            return new SessionDeletionResult(Found: 0, Deleted: 0);
        }

        var deleted = await dbContext.MfaSessions
            .Where(x => doomed.Contains(x.Id))
            .ExecuteDeleteAsync(cancellationToken);

        // Both counts are reported because they legitimately differ: a second instance sweeping at the same
        // time can delete some of these rows between the select and the delete. The caller loops on Found.
        return new SessionDeletionResult(doomed.Count, deleted);
    }
}