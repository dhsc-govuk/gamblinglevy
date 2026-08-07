using System.Text.Json;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Application.Common.Settings;
using Application.Mfa.Exceptions;
using Domain.AuditLogs;
using Domain.Users;
using Microsoft.Extensions.Logging;

namespace Application.Mfa;

/// <summary>
/// What happens when somebody gets a code wrong (BLUM-7239 AC3).
///
/// One collaborator rather than a copy in each handler. Five endpoints take a user-supplied code and answer
/// right or wrong, and the sequence they all need — count the failure, write its audit row, and if that
/// counted attempt applied the lock then revoke every live session and record that too — is exactly the kind
/// of thing that drifts when it is written five times. Leaving any endpoint out is not a cosmetic gap
/// either: a lock that only covers /mfa/verify is bypassed by guessing at /mfa/disable instead.
///
/// It does not touch the per-minute damper. Both thresholds are five by default, so the damper's budget is
/// spent at the instant the lock applies and every attempt from here on is refused by whichever control
/// answers first — which has to be the lock, or the caller is told to wait a minute for a thirty-minute
/// refusal. This was briefly done here by clearing the damper as the lock went on, which only covered the
/// single next attempt: the handlers charge the damper again on each retry, so a sixth one inside the same
/// minute went back to answering MFA_RATE_LIMITED. <see cref="MfaAttemptGate"/> now consults the lock
/// whenever the damper refuses, which is the same property without a counter to keep in step.
/// </summary>
public class MfaFailureRecorder(
    IUserMfaRepository userMfaRepository,
    IAuditLogRepository auditLogRepository,
    IMfaSessionService sessionService,
    IDateTimeProvider dateTimeProvider,
    ApplicationSettings settings,
    ILogger<MfaFailureRecorder> logger)
{
    /// <summary>
    /// Records one incorrect code and returns the exception the handler should answer with —
    /// <paramref name="invalidCode"/> normally, or <see cref="MfaLockedOutException"/> when this attempt was
    /// the one that locked the account.
    /// </summary>
    /// <param name="failedAuditAction">What kind of credential was wrong, e.g. AuditAction.MfaVerifyFailed.</param>
    /// <param name="endpoint">
    /// Which surface it was wrong on, e.g. "mfa/disable". Separate from the action on purpose: five endpoints
    /// take a code and most of them are checking a TOTP, so the action alone cannot say where the guessing
    /// happened. It used to be populated from the action, which meant the lockout row reported its endpoint as
    /// "mfa.verify.failed" and an administrator reading the trail was pointed at the wrong control.
    /// </param>
    /// <remarks>
    /// Takes no CancellationToken, deliberately. Everything here is the security trail for an attack in
    /// progress, and the request it belongs to is one an attacker controls: a caller who submits a guess and
    /// drops the socket without reading the response — the cheapest way to brute-force — would otherwise
    /// cancel the write that records their attempt, so the counter would never reach the threshold, the
    /// lockout would never apply, and nothing would be audited. The sibling RateLimitEventRecorder passes
    /// CancellationToken.None for the same reason; this one used to take the request's token and did not.
    /// </remarks>
    public async Task<MfaException> RecordAsync(
        UserId userId,
        UserMfa mfa,
        string failedAuditAction,
        string endpoint,
        MfaException invalidCode)
    {
        var locked = mfa.RecordFailedAttempt(settings.Mfa.MaxFailedAttempts, settings.Mfa.LockoutMinutes);

        await userMfaRepository.UpdateAsync(mfa, CancellationToken.None);

        await auditLogRepository.AppendAsync(
            AuditLog.Create(
                userId,
                AuditTargetType.User,
                userId.Value,
                failedAuditAction,
                before: null,
                after: JsonSerializer.Serialize(new { endpoint, failedAttempts = mfa.FailedAttempts })),
            CancellationToken.None);

        if (!locked || mfa.LockedUntil is null)
        {
            return invalidCode;
        }

        await this.RevokeSessionsAsync(userId);

        await auditLogRepository.AppendAsync(
            AuditLog.Create(
                userId,
                AuditTargetType.User,
                userId.Value,
                AuditAction.MfaLockedOut,
                before: null,
                after: JsonSerializer.Serialize(new
                {
                    failedAttempts = mfa.FailedAttempts,
                    lockedUntil = mfa.LockedUntil,
                    endpoint,
                })),
            CancellationToken.None);

        return new MfaLockedOutException(
            userId,
            mfa.LockedUntil.Value,
            MfaRetryAfter.Seconds(mfa.LockedUntil, dateTimeProvider.GetUtcNow(), settings.Mfa.LockoutMinutes * 60));
    }

    /// <summary>
    /// This is what makes the lock mean "requiring re-auth" rather than merely "no more guesses". Every other
    /// tab holding an mfa_session cookie gets 401 SESSION_REVOKED on its next request, which the client
    /// already turns into a sign-out — so no new client machinery is needed for it.
    ///
    /// Best-effort on purpose. It is several round trips, and it runs on a request that is already committed
    /// to answering 429; letting a revocation failure escape would turn a refusal the service meant into a
    /// 500 it did not. The lock itself is already persisted by this point, so the security outcome survives.
    /// </summary>
    private async Task RevokeSessionsAsync(UserId userId)
    {
        try
        {
            await sessionService.RevokeAllUserSessionsAsync(userId);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to revoke sessions after locking out user {UserId}", userId.Value);
        }
    }
}