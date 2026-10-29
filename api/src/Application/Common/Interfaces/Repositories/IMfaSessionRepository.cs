using Domain.Users;

namespace Application.Common.Interfaces.Repositories;

/// <summary>
/// The outcome of one bounded retention delete: how many records the cutoff matched, and how many this call
/// removed. A sweep loops while <paramref name="Found"/> fills the batch, never on <paramref name="Deleted"/>.
/// </summary>
public record SessionDeletionResult(int Found, int Deleted);

public interface IMfaSessionRepository
{
    Task<MfaSession?> GetByIdAsync(MfaSessionId sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every session of the user's that has not been revoked, including ones that have already passed their
    /// absolute or idle window but have not yet been destroyed by a request presenting them. Use this for a
    /// belt-and-braces sweep (logout, deactivation, suspension); use GetActiveByUserIdAsync for anything the
    /// user is shown or that counts towards the concurrency cap.
    /// </summary>
    Task<List<MfaSession>> GetByUserIdAsync(UserId userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The user's sessions that could still carry a request right now — not revoked and inside both windows,
    /// most recently active first. Ordered so the caller can evict from the tail (BLUM-7241).
    /// </summary>
    Task<List<MfaSession>> GetActiveByUserIdAsync(UserId userId, CancellationToken cancellationToken = default);
    Task<MfaSession> AddAsync(MfaSession session, CancellationToken cancellationToken = default);
    Task<MfaSession> UpdateAsync(MfaSession session, CancellationToken cancellationToken = default);

    /// <summary>
    /// Destroys a session that has timed out, recording why. Returns the number of rows changed: 0 means
    /// another request got there first, which is how the caller keeps the audit entry exactly-once.
    /// </summary>
    Task<int> TryRevokeTimedOutSessionAsync(MfaSessionId sessionId, MfaSessionEndReason reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes a live session, returning the number of rows changed: 0 means it was already revoked, which is
    /// how a caller keeps one revocation worth one audit entry when two requests race to end the same session.
    /// </summary>
    Task<int> TryRevokeSessionAsync(MfaSessionId sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Slides the idle window. Targets the single column on purpose — a whole-entity save here would write
    /// is_revoked back from a copy read before the update and could resurrect a session that a concurrent
    /// logout, deactivation or suspension had just revoked.
    /// </summary>
    Task<int> TryUpdateLastActivityAsync(MfaSessionId sessionId, DateTime lastActivityAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves the session's last-seen address (BLUM-7848), conditional on it still being
    /// <paramref name="expectedHash"/> (null meaning "never moved"). Returns the rows changed: 0 means a
    /// concurrent request already recorded this transition, which is how one move stays worth one audit row.
    /// Targets only the two columns, for the same reason TryUpdateLastActivityAsync does.
    /// </summary>
    Task<int> TryUpdateLastSeenIpAsync(MfaSessionId sessionId, string? expectedHash, string newHash, string newIpAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes session records — anyone's — whose absolute expiry passed before <paramref name="endedBefore"/>,
    /// at most <paramref name="batchSize"/> of them, returning how many went. This is a data-retention
    /// control, not a cache eviction: since BLUM-7241 the rows carry an IP address and a device description,
    /// so keeping them forever means retaining personal data with no stated period.
    ///
    /// Deliberately not scoped to one user. It used to be, piggybacked on that user's next sign-in, which
    /// meant the records of people who had left the organisation — the ones with least justification for
    /// being kept — were the only ones never deleted.
    ///
    /// The cutoff has to sit well beyond SessionExpiryHours — see MfaSettings.SessionRetentionDays for why
    /// deleting a recently-ended session breaks the SESSION_* envelopes. Audit history is unaffected:
    /// audit_logs.target_id is a plain Guid with no foreign key to this table.
    /// </summary>
    /// <returns>
    /// How many records matched the cutoff (<c>Found</c>, capped at <paramref name="batchSize"/>) and how
    /// many this call actually removed (<c>Deleted</c>). They differ when another instance deleted some of
    /// the same rows first, and the caller has to loop on <c>Found</c> — looping on <c>Deleted</c> reads a
    /// concurrent sweep as "nothing left" and abandons the backlog.
    /// </returns>
    Task<SessionDeletionResult> DeleteEndedSessionsAsync(DateTime endedBefore, int batchSize, CancellationToken cancellationToken = default);
}