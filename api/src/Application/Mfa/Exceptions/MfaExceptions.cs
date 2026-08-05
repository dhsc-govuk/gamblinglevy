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

public class MfaRateLimitException(UserId userId, DateTime? lockedUntil = null)
    : MfaException(userId, $"Too many MFA attempts for user {userId}. Try again later.")
{
    public DateTime? LockedUntil { get; } = lockedUntil;
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