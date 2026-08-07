using Application.Common.Interfaces;
using Application.Common.Settings;
using Domain.Users;
using Infrastructure.Security;
using Microsoft.Extensions.Caching.Memory;

namespace Infrastructure.Mfa;

/// <summary>
/// Per-user damper on MFA attempts, built on the shared <see cref="FixedWindowCounter"/> (BLUM-7239 AC3).
///
/// It used to keep its own counter, and that counter had all three of the defects FixedWindowCounter now
/// documents. IMemoryCache.GetOrCreate handed out a mutable object which was incremented without
/// synchronisation, so two concurrent attempts each built their own and one was discarded — five per minute
/// was not the cap actually applied. The expiry was re-armed on every write, so the window <em>slid</em>: a
/// caller trickling one attempt just inside each minute stayed limited indefinitely while the counter never
/// aged out. And the limit and window were consts, so no environment could tune them and no test could
/// disable them.
/// </summary>
public class MfaRateLimitService(IMemoryCache cache, IDateTimeProvider dateTimeProvider, ApplicationSettings settings)
    : IMfaRateLimitService
{
    private readonly FixedWindowCounter counter = new(dateTimeProvider);

    public MfaRateLimitDecision Reserve(UserId userId)
    {
        var limit = settings.Mfa.MaxAttemptsPerWindow;

        // 0 disables the damper. The lockout tests depend on this: a damper and a lockout that both trip at
        // five would mean the damper always answered first and the lockout could never be observed.
        if (limit <= 0)
        {
            return new MfaRateLimitDecision(true, 0);
        }

        var window = TimeSpan.FromSeconds(settings.Mfa.RateLimitWindowSeconds <= 0 ? 60 : settings.Mfa.RateLimitWindowSeconds);

        return this.counter.InTransaction(() =>
        {
            var state = this.counter.Increment(cache, GetCacheKey(userId), window);

            return state.Count <= limit
                ? new MfaRateLimitDecision(true, 0)
                : new MfaRateLimitDecision(false, this.counter.SecondsUntil(state.WindowEndsAt));
        });
    }

    public void ResetAttempts(UserId userId)
    {
        this.counter.Remove(cache, GetCacheKey(userId));
    }

    private static string GetCacheKey(UserId userId) => $"mfa_rate_limit_{userId.Value}";
}