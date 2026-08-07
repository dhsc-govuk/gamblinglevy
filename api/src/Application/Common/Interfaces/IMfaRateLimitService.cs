using Domain.Users;

namespace Application.Common.Interfaces;

/// <summary>Whether an MFA attempt may proceed, and how long to wait if not.</summary>
public readonly record struct MfaRateLimitDecision(bool Allowed, int RetryAfterSeconds);

/// <summary>
/// The short per-user damper in front of the persistent lockout on user_mfas (BLUM-7239 AC3).
///
/// Two things it deliberately is not. It is not the control of record — its counters live in memory, so they
/// reset when the App Service unloads and would not be shared across instances; that is why the lockout it
/// fronts is persisted instead. And it is not a duplicate of the per-address limiter in the middleware: this
/// one is scoped to a user, so it sees one account attacked from many addresses, which the other
/// structurally cannot. Both apply to the MFA endpoints, on purpose.
///
/// There is no GetRemainingAttempts. There was, it was called from nowhere, and its only plausible use was
/// to tell the caller how many guesses remained before the lock — which on a verification endpoint is
/// publishing the control's own state to the party it is defending against.
/// </summary>
public interface IMfaRateLimitService
{
    /// <summary>
    /// Counts one attempt and says whether it may proceed.
    ///
    /// Checking and counting are one operation because they were two, and between them a second request
    /// could read a budget the first had not yet spent. Note that this counts attempts, not failures: five
    /// wrong codes in a window refuse a sixth even if the sixth is right, which is the point of a damper on
    /// a guessing endpoint. <see cref="ResetAttempts"/> clears the budget on success, so nothing changes for
    /// somebody who simply mistyped.
    /// </summary>
    MfaRateLimitDecision Reserve(UserId userId);

    void ResetAttempts(UserId userId);
}