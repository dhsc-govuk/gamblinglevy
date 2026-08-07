using Application.Common.Interfaces;
using Application.Common.Security;
using Application.Common.Settings;
using Microsoft.Extensions.Caching.Memory;

namespace Infrastructure.Security;

/// <summary>
/// Fixed-window per-partition request limiting over an in-memory counter (BLUM-7239 AC4).
///
/// It owns a <em>private</em> cache rather than sharing the application's IMemoryCache, which is the single
/// most important decision in this class. The shared cache has no size limit and also holds the five-minute
/// user-claims entries and the MFA counters; a caller varying the address they claim writes one entry per
/// request, so pointing this at the shared cache would evict user claims under exactly the load it is meant
/// to shed — forcing a UserProfileRepository read per request and turning the limiter into an amplifier.
///
/// Being bounded, the cache compacts under pressure, and an evicted partition's count restarts. The limiter
/// therefore fails <em>open</em>: an attacker who fills it buys themselves a reset. That is the right way for
/// a fair-use control to fail — the alternative, refusing traffic it can no longer account for, hands anyone
/// a way to take the service down for everybody. What actually bounds that case is the unpartitioned Global
/// policy, which needs one entry however many addresses are claimed.
/// </summary>
public sealed class MemoryCacheRateLimiter : IRateLimiter, IDisposable
{
    private readonly FixedWindowCounter counter;
    private readonly ApplicationSettings settings;

    private MemoryCache cache;

    public MemoryCacheRateLimiter(IDateTimeProvider dateTimeProvider, ApplicationSettings settings)
    {
        this.counter = new FixedWindowCounter(dateTimeProvider);
        this.settings = settings;
        this.cache = this.CreateCache();
    }

    public RateLimitDecision Acquire(string partitionKey, RateLimitPolicy policy, RateLimitPolicySettings policySettings)
    {
        var limit = policySettings.PermitLimit;

        // Unlimited short-circuits before touching the cache. Counting a policy nobody is capping would grow
        // partitions for no decision, and the middleware reads Limit == 0 as "emit no headers" — advertising
        // RateLimit-Limit: 0 would tell a conforming client it may never call again.
        if (limit <= 0)
        {
            return new RateLimitDecision(true, policy, 0, 0, 1, 0);
        }

        return this.counter.InTransaction(() =>
        {
            var state = this.counter.Increment(this.cache, partitionKey, policySettings.Window);
            var resetsIn = this.counter.SecondsUntil(state.WindowEndsAt);

            // Count is the hit just recorded, so it reaches Limit exactly on the last permitted request and
            // Limit + 1 on the first refused one. That subtraction is what gives the recorder its
            // "is this the first rejection in this window" test for free, with no second counter to keep in
            // step — a burst of ten thousand yields RejectionsInWindow 1, 2, 3 … and only the 1 is audited.
            var allowed = state.Count <= limit;

            return new RateLimitDecision(
                Allowed: allowed,
                Policy: policy,
                Limit: limit,
                Remaining: Math.Max(0, limit - state.Count),
                ResetsInSeconds: resetsIn,
                RejectionsInWindow: allowed ? 0 : state.Count - limit);
        });
    }

    public void Clear()
    {
        this.cache.Clear();
    }

    public void Dispose()
    {
        this.cache.Dispose();
    }

    private MemoryCache CreateCache()
    {
        return new MemoryCache(new MemoryCacheOptions
        {
            // Every entry declares Size 1, so this is a partition count rather than a byte count.
            SizeLimit = this.settings.RateLimiting.MaxTrackedPartitions,
        });
    }
}