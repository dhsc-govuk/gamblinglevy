using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Application.Common.Settings;
using Application.Mfa.Exceptions;
using Domain.Users;
using LanguageExt.UnsafeValueAccess;

namespace Application.Mfa;

/// <summary>
/// The first thing every code-taking endpoint does: spend one unit of the per-minute damper, and say why the
/// request is refused when there is nothing left to spend (BLUM-7239 AC3).
///
/// One collaborator rather than a copy in each handler, for the same reason as <see cref="MfaFailureRecorder"/>
/// — and because the interesting half is the part that is easy to leave out. The damper has to be charged
/// before the account is loaded, or it is not a damper on load; but that means the refusal it produces is
/// decided before anything knows whether the account is locked. Answering it as MFA_RATE_LIMITED whenever the
/// budget happens to be spent is wrong: the client treats that envelope as "wait a moment", not as a
/// session-ended reason, so a user locked out for thirty minutes was told to wait one and was never signed
/// out. So a spent budget asks the persisted record before it answers, and the lock wins when both apply.
///
/// The lookup only happens on the refusal path, and only when a lockout is configured at all. It costs one
/// indexed read on a request that was going to make the same read had the damper let it through.
/// </summary>
public class MfaAttemptGate(
    IMfaRateLimitService rateLimitService,
    IUserMfaRepository userMfaRepository,
    IDateTimeProvider dateTimeProvider,
    ApplicationSettings settings)
{
    /// <summary>
    /// Charges the damper for one attempt and returns the exception the handler should answer with, or
    /// <c>null</c> when the attempt may proceed.
    /// </summary>
    public async Task<MfaException?> ReserveAsync(UserId userId, CancellationToken cancellationToken)
    {
        var reservation = rateLimitService.Reserve(userId);

        if (reservation.Allowed)
        {
            return null;
        }

        var lockedUntil = await this.GetLiveLockAsync(userId, cancellationToken);

        if (lockedUntil is null)
        {
            return new MfaRateLimitException(userId, lockedUntil: null, reservation.RetryAfterSeconds);
        }

        return new MfaLockedOutException(
            userId,
            lockedUntil.Value,
            MfaRetryAfter.Seconds(lockedUntil, dateTimeProvider.GetUtcNow(), settings.Mfa.LockoutMinutes * 60));
    }

    private async Task<DateTime?> GetLiveLockAsync(UserId userId, CancellationToken cancellationToken)
    {
        // 0 disables the lockout, which is how several test classes reach the damper on its own. There is no
        // lock to find, so do not pay for the read.
        if (settings.Mfa.MaxFailedAttempts <= 0)
        {
            return null;
        }

        var userMfaOption = await userMfaRepository.GetByUserIdAsync(userId, cancellationToken);

        if (userMfaOption.IsNone)
        {
            return null;
        }

        var mfa = userMfaOption.ValueUnsafe();

        return mfa.IsLockedOut() ? mfa.LockedUntil : null;
    }
}