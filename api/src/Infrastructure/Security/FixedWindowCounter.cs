using Application.Common.Interfaces;
using Microsoft.Extensions.Caching.Memory;

namespace Infrastructure.Security;

/// <summary>A counter's state after an operation.</summary>
/// <param name="Count">Hits recorded in the window that is currently open.</param>
/// <param name="WindowEndsAt">
/// When that window closes, judged against the injected clock. For an absent or elapsed counter this is the
/// current instant, so a caller can always subtract without a null check.
/// </param>
public readonly record struct FixedWindowState(int Count, DateTime WindowEndsAt);

/// <summary>
/// Counting in fixed windows over an <see cref="IMemoryCache"/>, shared by everything in this codebase that
/// bounds a rate: the audit-flood caps (BLUM-7240) and the request limiter (BLUM-7239).
///
/// It exists because the same four mistakes keep being available, and three of them were made here first:
///
/// 1. Bucket on the route <em>template</em>, never the request path. A path carries route values, so
///    `PUT /activities/{a fresh GUID}` mints a distinct counter per request — the caller both slips past
///    their own cap and grows an unbounded cache. Enforced by callers; restated here because this is the
///    class they would misuse.
/// 2. Nothing is counted unless it is admitted. Charging a shared budget for hits that were already
///    suppressed lets one noisy caller exhaust it for everybody else.
/// 3. Counts are plain ints read and written under one gate, not a mutable object handed out by
///    <c>GetOrCreate</c>. Two concurrent misses each build their own counter and one increment is discarded,
///    so the cap the setting promises is not the cap being applied.
/// 4. The cache is never handed an <em>absolute</em> expiry derived from the injected clock. IMemoryCache
///    judges absolute expiry against the system clock and silently declines to store an entry that has
///    already expired on arrival — so under a test clock set in the past it stores nothing at all, every
///    counter reads back zero, and the limiter admits everything. The window's end therefore travels inside
///    the value and is enforced by <see cref="Current"/>; the cache entry's own TTL is relative and exists
///    only to reclaim memory for a key nobody asks about again.
///
/// The gate is per-instance rather than static, so two subsystems counting different things do not contend.
/// That is only correct while every consumer is a singleton — a scoped consumer would hold a gate that
/// synchronises nothing across requests. It is a plain <see cref="Monitor"/> lock, so
/// <see cref="InTransaction{T}"/> composes with the individual operations without deadlocking.
///
/// Note that <see cref="InputRejectionThrottle"/> justified serialising on "rejections are rare, so it costs
/// nothing worth measuring". That justification does not carry over — the limiter runs on every request. The
/// critical section is a dictionary lookup and an integer add, which is still the right trade at this
/// service's traffic; if it ever stops being, the upgrade is a striped array of gates indexed by key hash,
/// not a lock-free rewrite.
/// </summary>
public sealed class FixedWindowCounter(IDateTimeProvider dateTimeProvider)
{
    private readonly object gate = new();

    /// <summary>
    /// Runs <paramref name="body"/> with the gate held, so a sequence of reads and writes is atomic as a
    /// whole rather than merely individually. Needed wherever a decision is made from several counters at
    /// once — reading a per-actor count and a global count separately, then writing both, is a race without
    /// this.
    /// </summary>
    public T InTransaction<T>(Func<T> body)
    {
        lock (this.gate)
        {
            return body();
        }
    }

    /// <summary>
    /// Adds one to the counter in the window that is currently open, opening one if none is.
    ///
    /// The window's end is read back from the existing entry and deliberately <em>not</em> re-extended.
    /// Re-arming it on every hit would make the window slide, so a caller trickling one request just inside
    /// each window would stay limited indefinitely and the counter would never age out.
    /// </summary>
    public FixedWindowState Increment(IMemoryCache cache, string key, TimeSpan window)
    {
        lock (this.gate)
        {
            var current = this.Current(cache, key);
            var endsAt = current?.WindowEndsAt ?? dateTimeProvider.GetUtcNow().Add(window);
            var count = (current?.Count ?? 0) + 1;

            return this.Store(cache, key, count, endsAt, window);
        }
    }

    /// <summary>
    /// Adds one to a counter whose window restarts on every write.
    ///
    /// Correct for a tally that answers "how many since the last time we reported", which has to outlive the
    /// rate window it is reported alongside; wrong for a rate counter, which is why it is a separate method.
    /// A relative cache TTL re-applied on every write already slides, so the two agree here.
    /// </summary>
    public FixedWindowState IncrementSliding(IMemoryCache cache, string key, TimeSpan window)
    {
        lock (this.gate)
        {
            var count = (this.Current(cache, key)?.Count ?? 0) + 1;
            var endsAt = dateTimeProvider.GetUtcNow().Add(window);

            return this.Store(cache, key, count, endsAt, window);
        }
    }

    /// <summary>The counter in the window that is currently open, or zero once that window has passed.</summary>
    public FixedWindowState Peek(IMemoryCache cache, string key)
    {
        lock (this.gate)
        {
            return this.Current(cache, key)
                ?? new FixedWindowState(0, dateTimeProvider.GetUtcNow());
        }
    }

    /// <summary>Drops a counter, so the next hit opens a fresh window.</summary>
    public void Remove(IMemoryCache cache, string key)
    {
        lock (this.gate)
        {
            cache.Remove(key);
        }
    }

    /// <summary>
    /// Seconds until the window closes, floored at one.
    ///
    /// Never zero: a Retry-After or RateLimit-Reset of 0 invites a conforming client straight back into a
    /// tight retry loop, which is the opposite of what the header is for.
    /// </summary>
    public int SecondsUntil(DateTime windowEndsAt)
    {
        var remaining = (windowEndsAt - dateTimeProvider.GetUtcNow()).TotalSeconds;

        return remaining <= 1 ? 1 : (int)Math.Ceiling(remaining);
    }

    private FixedWindowState Store(IMemoryCache cache, string key, int count, DateTime endsAt, TimeSpan window)
    {
        var state = new FixedWindowState(count, endsAt);

        // Relative, never absolute — see lesson 4 on the class. A MemoryCacheEntryOptions carrying Size is
        // required by any cache configured with a SizeLimit, and ignored by one that is not, so the limiter's
        // private bounded cache and the shared unbounded one both accept this.
        cache.Set(key, state, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = window,
            Size = 1,
        });

        return state;
    }

    private FixedWindowState? Current(IMemoryCache cache, string key)
    {
        if (!cache.TryGetValue<FixedWindowState>(key, out var state))
        {
            return null;
        }

        return state.WindowEndsAt > dateTimeProvider.GetUtcNow() ? state : null;
    }
}