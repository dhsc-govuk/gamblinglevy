using Microsoft.Extensions.Caching.Memory;

namespace Infrastructure.Security;

/// <summary>How many audit_logs rows one kind of security event may cause.</summary>
/// <param name="MaxPerActorPerWindow">Rows one caller may generate against one endpoint. 0 means unlimited.</param>
/// <param name="MaxGlobalPerWindow">Ceiling across all callers, so a probe spread over many accounts cannot flood the table either. 0 means unlimited.</param>
/// <param name="Window">How long both budgets run for before they reset.</param>
public readonly record struct AuditThrottleLimits(int MaxPerActorPerWindow, int MaxGlobalPerWindow, TimeSpan Window);

/// <summary>The outcome of asking whether a security event may be written to audit_logs.</summary>
/// <param name="Admitted">False when the caller should degrade to telemetry and logs only.</param>
/// <param name="SuppressedSincePrevious">
/// How many events were suppressed since the last admitted one, carried onto the admitted row so a burst
/// stays visible in the audit trail as a number.
/// </param>
public readonly record struct AuditThrottleDecision(bool Admitted, int SuppressedSincePrevious);

/// <summary>
/// Bounds how many audit_logs rows a security event may cause, for any kind of event.
///
/// audit_logs rows are kept for the whole retention period (for ever by default) and every append commits its own transaction, so without a cap the
/// caller chooses how many transactions the service commits and how much of the genuine user.* and mfa.*
/// trail gets buried. Suppression degrades an event to telemetry rather than dropping it — the burst still
/// arrives, as a number on the next admitted row.
///
/// <paramref name="scope"/> on <see cref="Reserve"/> is what keeps two kinds of event from spending each
/// other's budget. The global counter is namespaced by it, so a rate-limit flood cannot blank the
/// input-validation trail, and vice versa. Sharing one global key across scopes is the defect this parameter
/// exists to prevent, and <c>AuditFloodThrottleTests.ShouldKeepGlobalBudgetsSeparatePerScope</c> pins it.
/// </summary>
public sealed class AuditFloodThrottle(IMemoryCache cache, FixedWindowCounter counter)
{
    // The suppressed tally deliberately outlives the rate window. It answers "how many were dropped since the
    // last row we wrote", and the row that reports it can only be written once the rate window has reset — so
    // a tally sharing that window would always have expired by the time anything could report it.
    private static readonly TimeSpan SuppressedRetention = TimeSpan.FromHours(24);

    public AuditThrottleDecision Reserve(string scope, string actorKey, AuditThrottleLimits limits)
    {
        var actorCacheKey = $"{scope}_{actorKey}";
        var globalKey = $"{scope}_global";
        var suppressedKey = $"{actorCacheKey}_suppressed";

        // One transaction, because the decision reads two counters and then writes both. Taking them
        // separately would let two callers each see room under the global cap and both take the last row.
        return counter.InTransaction(() =>
        {
            var actorCount = counter.Peek(cache, actorCacheKey).Count;
            var globalCount = counter.Peek(cache, globalKey).Count;

            var actorCapped = limits.MaxPerActorPerWindow > 0 && actorCount >= limits.MaxPerActorPerWindow;
            var globalCapped = limits.MaxGlobalPerWindow > 0 && globalCount >= limits.MaxGlobalPerWindow;

            if (actorCapped || globalCapped)
            {
                // Nothing is charged to either budget here. Counting a suppressed event would let one noisy
                // caller exhaust the shared allowance and blank the trail for everyone else.
                counter.IncrementSliding(cache, suppressedKey, SuppressedRetention);

                return new AuditThrottleDecision(false, 0);
            }

            counter.Increment(cache, actorCacheKey, limits.Window);
            counter.Increment(cache, globalKey, limits.Window);

            var suppressed = counter.Peek(cache, suppressedKey).Count;
            counter.Remove(cache, suppressedKey);

            return new AuditThrottleDecision(true, suppressed);
        });
    }
}