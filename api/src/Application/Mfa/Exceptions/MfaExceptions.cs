using Domain.Users;

namespace Application.Mfa.Exceptions;

public abstract class MfaException(UserId userId, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public UserId UserId { get; } = userId;
}

public class MfaRequiredException(UserId userId)
    : MfaException(userId, $"MFA verification required for user {userId}");

public class MfaSetupRequiredException(UserId userId)
    : MfaException(userId, $"MFA setup required for user {userId}");

public class MfaNotEnabledException(UserId userId)
    : MfaException(userId, $"MFA is not enabled for user {userId}");

public class MfaAlreadyEnabledException(UserId userId)
    : MfaException(userId, $"MFA is already enabled for user {userId}");

public class MfaNotSetupException(UserId userId)
    : MfaException(userId, $"MFA has not been set up for user {userId}. User must complete MFA setup first.");

public class InvalidMfaCodeException(UserId userId)
    : MfaException(userId, $"Invalid MFA code provided for user {userId}");

/// <summary>
/// The short per-minute damper tripped. Recoverable within a minute, and distinct from
/// <see cref="MfaLockedOutException"/> so the two answer different codes on the wire — a client that cannot
/// tell them apart would tell a locked-out user to wait "a few minutes".
/// </summary>
public class MfaRateLimitException(UserId userId, DateTime? lockedUntil = null, int retryAfterSeconds = 60)
    : MfaException(userId, $"Too many MFA attempts for user {userId}. Try again later.")
{
    public DateTime? LockedUntil { get; } = lockedUntil;

    /// <summary>
    /// Populated by the handler, which is the only place that can see the configured window. The default is
    /// the default window, so an exception raised somewhere that has no settings still answers something
    /// truthful rather than nothing.
    /// </summary>
    public int RetryAfterSeconds { get; } = retryAfterSeconds;
}

/// <summary>
/// The account is locked after too many incorrect codes (BLUM-7239 AC3). Deliberately not derived from
/// <see cref="MfaRateLimitException"/>: the error factory dispatches on type, and a subclass would make the
/// order of the switch arms load-bearing.
/// </summary>
public class MfaLockedOutException(UserId userId, DateTime lockedUntil, int retryAfterSeconds)
    : MfaException(userId, $"MFA is locked for user {userId} until {lockedUntil:O}.")
{
    public DateTime LockedUntil { get; } = lockedUntil;

    public int RetryAfterSeconds { get; } = retryAfterSeconds;
}

public class MfaSetupPendingException(UserId userId)
    : MfaException(userId, $"MFA setup is pending for user {userId}. Please complete setup.");

public class MfaSetupExpiredException(UserId userId)
    : MfaException(userId, $"MFA setup has expired for user {userId}. Please start again.");

public class InvalidRecoveryCodeException(UserId userId)
    : MfaException(userId, $"Invalid recovery code provided for user {userId}");

/// <summary>
/// The session a user asked to revoke is not one of theirs. Deliberately raised for a session that does not
/// exist AND for one belonging to somebody else, so that the response cannot be used to discover whether a
/// given session id is real (BLUM-7241).
/// </summary>
public class MfaSessionNotFoundException(UserId userId, MfaSessionId sessionId)
    : MfaException(userId, $"No session {sessionId} for user {userId}")
{
    public MfaSessionId SessionId { get; } = sessionId;
}

public class MfaUnknownException(UserId userId, Exception innerException)
    : MfaException(userId, $"Unknown MFA error for user {userId}", innerException);