using Application.Common.Interfaces;
using Application.Common.Settings;
using Microsoft.Extensions.Caching.Memory;

namespace Infrastructure.Security;

/// <summary>The outcome of asking whether a security rejection may be written to audit_logs.</summary>
/// <param name="Admitted">False when the caller should degrade to telemetry and logs only.</param>
/// <param name="SuppressedSincePrevious">
/// How many rejections were suppressed since the last admitted one, carried onto the admitted row so a
/// burst stays visible in the audit trail as a number.
/// </param>
public readonly record struct InputRejectionThrottleDecision(bool Admitted, int SuppressedSincePrevious);

/// <summary>
/// Bounds how many audit_logs rows a rejected request may cause (BLUM-7240 AC8).
///
/// Split out of InputRejectionRecorder so it can be tested directly. It is the most intricate code in this
/// feature and the integration suite deliberately switches it off — with the caps live, whether an assertion
/// passed would depend on how many earlier tests happened to send hostile input.
///
/// Three things here are deliberate and were wrong in the first cut:
///
/// 1. The actor key is built from the route <em>template</em>, never the request path. The path contains
///    route values, so `PUT /activities/{a fresh GUID}` produced a distinct cache entry per request: an
///    authenticated caller could both slip past their own cap and grow an unbounded one-hour cache in a
///    process whose IMemoryCache has no size limit — and that cache also holds the MFA rate-limit counters.
/// 2. Nothing is counted unless the row is actually admitted. Charging the shared budget for suppressed
///    rejections let one noisy caller exhaust it and blank the trail for everyone else.
/// 3. Counters are plain ints read and written under one process-wide lock, rather than a mutable object
///    handed out by GetOrCreate. Two concurrent misses used to each construct a counter and one increment
///    was discarded — so the cap the setting promises was not the cap being applied. Rejections are rare, so
///    serialising this decision costs nothing worth measuring.
/// </summary>
public sealed class InputRejectionThrottle(IMemoryCache cache, IDateTimeProvider dateTimeProvider)
{
    private const string KeyPrefix = "input_rejection_audit";
    private const string GlobalKey = KeyPrefix + "_global";

    private static readonly TimeSpan Window = TimeSpan.FromHours(1);

    // The suppressed tally deliberately outlives the rate window. It answers "how many were dropped since
    // the last row we wrote", and the row that reports it can only be written once the rate window has
    // reset — so a tally sharing that window would always have expired by the time anything could report it.
    private static readonly TimeSpan SuppressedRetention = TimeSpan.FromHours(24);

    // Static because the recorder is scoped: a per-instance gate would synchronise nothing across requests.
    private static readonly object Gate = new();

    public InputRejectionThrottleDecision Reserve(string actorKey, InputValidationSettings limits)
    {
        var actorCacheKey = $"{KeyPrefix}_{actorKey}";
        var suppressedKey = $"{actorCacheKey}_suppressed";

        lock (Gate)
        {
            var actorCount = Read(actorCacheKey);
            var globalCount = Read(GlobalKey);

            var actorCapped = limits.MaxAuditRowsPerActorPerHour > 0
                && actorCount >= limits.MaxAuditRowsPerActorPerHour;

            var globalCapped = limits.MaxAuditRowsPerHour > 0
                && globalCount >= limits.MaxAuditRowsPerHour;

            if (actorCapped || globalCapped)
            {
                WriteSliding(suppressedKey, Read(suppressedKey) + 1);
                return new InputRejectionThrottleDecision(false, 0);
            }

            Write(actorCacheKey, actorCount + 1);
            Write(GlobalKey, globalCount + 1);

            var suppressed = Read(suppressedKey);
            cache.Remove(suppressedKey);

            return new InputRejectionThrottleDecision(true, suppressed);
        }
    }

    /// <summary>
    /// The count in the current window, or zero once that window has passed.
    ///
    /// Expiry is judged against the injected clock rather than left to the cache's own eviction, so the
    /// window is a property of this class and can be tested by moving the clock. The cache entry carries a
    /// relative TTL of its own, but only so memory is reclaimed for a key nobody asks about again.
    /// </summary>
    private int Read(string key)
    {
        return Current(key)?.Count ?? 0;
    }

    /// <summary>
    /// Writes the count back under a <em>fixed</em> window. Re-setting the window's end on every increment
    /// would make it slide, so a caller trickling one rejection an hour would stay suppressed indefinitely
    /// and the counter would never age out.
    ///
    /// The end of that window travels inside the value and is enforced by <see cref="Current"/> against the
    /// injected clock; the cache entry's own TTL is <em>relative</em>, and deliberately so. IMemoryCache
    /// judges an absolute expiry against the system clock and silently declines to store an entry that is
    /// already expired when it arrives — so an absolute deadline derived from the injected clock stores
    /// nothing at all whenever the two clocks disagree, and every counter reads back as zero.
    /// </summary>
    private void Write(string key, int count)
    {
        var expiresAt = Current(key)?.ExpiresAt ?? dateTimeProvider.GetUtcNow().Add(Window);

        cache.Set(key, new CounterWindow(count, expiresAt), Window);
    }

    /// <summary>
    /// Writes a counter whose window restarts on every write.
    ///
    /// Correct for the suppressed tally and wrong for the rate counters, which is why it is a separate
    /// method. The tally answers "how many were dropped since the last row we wrote", so it has to survive
    /// for as long as suppression continues — ageing it out on the rate window would silently understate the
    /// burst on the very row that reports it. It is cleared explicitly once reported.
    ///
    /// The cache TTL is relative for the same reason as <see cref="Write"/>; here it is also meant to slide,
    /// which is what a relative TTL re-applied on every write already does.
    /// </summary>
    private void WriteSliding(string key, int count)
    {
        var expiresAt = dateTimeProvider.GetUtcNow().Add(SuppressedRetention);

        cache.Set(key, new CounterWindow(count, expiresAt), SuppressedRetention);
    }

    private CounterWindow? Current(string key)
    {
        if (!cache.TryGetValue<CounterWindow>(key, out var window) || window is null)
        {
            return null;
        }

        return window.ExpiresAt > dateTimeProvider.GetUtcNow() ? window : null;
    }

    private sealed record CounterWindow(int Count, DateTime ExpiresAt);
}