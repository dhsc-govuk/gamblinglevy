using FluentAssertions;
using Infrastructure.Security;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace Application.Tests.Unit.Security;

/// <summary>
/// BLUM-7239 — the counting primitive behind both the audit-flood caps and the request limiter.
///
/// Tested here rather than through the API for the same reason InputRejectionThrottleTests gives: the
/// integration suite switches the limiter off, because counters live in a process-wide cache that outlives
/// a test and an assertion's result would otherwise depend on how many requests earlier tests happened to
/// send. Switching a control off globally is not a reason to leave it uncovered.
///
/// Every case below is a way this class, or the one it was extracted from, has actually failed.
/// </summary>
public class FixedWindowCounterTests
{
    private const string Key = "rl_Write_POST /organisations/local-authorities_203.0.113.1";
    private const string OtherKey = "rl_Write_POST /activities_203.0.113.2";

    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    [Fact]
    public void ShouldCountUpWithinOneWindow()
    {
        var counter = Build(out _, out var cache);

        var counts = Enumerable.Range(0, 4)
            .Select(_ => counter.Increment(cache, Key, Window).Count)
            .ToList();

        counts.Should().Equal(1, 2, 3, 4);
    }

    [Fact]
    public void ShouldStoreAndReadBack_WhenTheInjectedClockIsFarFromTheSystemClock()
    {
        // The regression fixed at 76a3abd. A counter that derives an absolute cache expiry from the injected
        // clock hands IMemoryCache a deadline that has already passed in real time; the cache declines to
        // store the entry, every read comes back zero, and the limit is never reached. Nothing else in this
        // file would notice — which is precisely how it survived being written.
        var counter = Build(out var clock, out var cache);
        clock.UtcNow = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        counter.Increment(cache, Key, Window);
        counter.Increment(cache, Key, Window);

        counter.Peek(cache, Key).Count.Should().Be(2, "the window's end travels in the value, not in the cache's expiry");
    }

    [Fact]
    public void ShouldUseAFixedWindow_NotASlidingOne()
    {
        // A window re-armed on every hit would never age out, so a caller trickling one request just inside
        // each window would stay limited forever.
        var counter = Build(out var clock, out var cache);

        counter.Increment(cache, Key, Window);

        for (var i = 0; i < 5; i++)
        {
            clock.UtcNow = clock.UtcNow.AddSeconds(10);
            counter.Increment(cache, Key, Window);
        }

        counter.Peek(cache, Key).Count.Should().Be(6);

        // Past the original minute, counted from the first hit rather than the last.
        clock.UtcNow = clock.UtcNow.AddSeconds(11);

        counter.Peek(cache, Key).Count.Should().Be(0);
        counter.Increment(cache, Key, Window).Count.Should().Be(1, "a fresh window opens");
    }

    [Fact]
    public void ShouldRestartTheWindowOnEveryWrite_WhenSliding()
    {
        var counter = Build(out var clock, out var cache);

        counter.IncrementSliding(cache, Key, Window);

        for (var i = 0; i < 5; i++)
        {
            clock.UtcNow = clock.UtcNow.AddSeconds(50);
            counter.IncrementSliding(cache, Key, Window);
        }

        counter.Peek(cache, Key).Count.Should().Be(6, "each write pushed the end out by a full window");
    }

    [Fact]
    public void ShouldReportZero_ForAKeyNeverSeen()
    {
        var counter = Build(out var clock, out var cache);

        var state = counter.Peek(cache, Key);

        state.Count.Should().Be(0);
        state.WindowEndsAt.Should().Be(clock.UtcNow, "callers subtract from this without a null check");
    }

    [Fact]
    public void ShouldKeepDistinctKeysIndependent()
    {
        var counter = Build(out _, out var cache);

        counter.Increment(cache, Key, Window);
        counter.Increment(cache, Key, Window);
        counter.Increment(cache, OtherKey, Window);

        counter.Peek(cache, Key).Count.Should().Be(2);
        counter.Peek(cache, OtherKey).Count.Should().Be(1);
    }

    [Fact]
    public void ShouldOpenAFreshWindow_AfterRemove()
    {
        var counter = Build(out _, out var cache);

        counter.Increment(cache, Key, Window);
        counter.Increment(cache, Key, Window);
        counter.Remove(cache, Key);

        counter.Peek(cache, Key).Count.Should().Be(0);
    }

    [Fact]
    public void ShouldNeverReportAResetOfZeroSeconds()
    {
        // A Retry-After or RateLimit-Reset of 0 puts a conforming client straight back into a tight retry
        // loop against the limit it just hit.
        var counter = Build(out var clock, out _);

        counter.SecondsUntil(clock.UtcNow).Should().Be(1);
        counter.SecondsUntil(clock.UtcNow.AddSeconds(-30)).Should().Be(1, "an elapsed window still answers at least a second");
        counter.SecondsUntil(clock.UtcNow.AddMilliseconds(1)).Should().Be(1);
    }

    [Fact]
    public void ShouldRoundThePartialSecondUp_WhenReportingReset()
    {
        // Rounding down would advertise a window as over while it is still closed, so a client that trusted
        // the header would be rejected again the moment it retried.
        var counter = Build(out var clock, out _);

        counter.SecondsUntil(clock.UtcNow.AddMilliseconds(41_200)).Should().Be(42);
    }

    [Fact]
    public void ShouldNotLoseIncrementsUnderConcurrency()
    {
        // The bug still live in MfaRateLimitService: IMemoryCache.GetOrCreate hands out a mutable object, two
        // concurrent misses each build their own, and one increment is discarded — so the cap the setting
        // promises is not the cap being applied.
        var counter = Build(out _, out var cache);

        Parallel.For(0, 500, _ => counter.Increment(cache, Key, Window));

        counter.Peek(cache, Key).Count.Should().Be(500);
    }

    [Fact]
    public void ShouldWorkAgainstACacheWithASizeLimit()
    {
        // The limiter owns a bounded cache so an attacker varying their claimed IP cannot grow it without
        // limit. A cache configured with SizeLimit rejects any entry that does not declare a Size, so this
        // asserts the counter always does.
        var counter = new FixedWindowCounter(new TestClock());
        using var bounded = new MemoryCache(new MemoryCacheOptions { SizeLimit = 64 });

        var act = () => counter.Increment(bounded, Key, Window);

        act.Should().NotThrow();
        counter.Peek(bounded, Key).Count.Should().Be(1);
    }

    [Fact]
    public void ShouldRunACompoundDecisionAtomically()
    {
        // InTransaction is what lets AuditFloodThrottle read two counters and then write both without a
        // second caller slipping between the read and the write. The inner Increment takes the same gate, so
        // this also pins that the lock is reentrant.
        var counter = Build(out _, out var cache);

        Parallel.For(0, 200, _ => counter.InTransaction(() =>
        {
            var seen = counter.Peek(cache, Key).Count;
            counter.Increment(cache, Key, Window);
            return seen;
        }));

        counter.Peek(cache, Key).Count.Should().Be(200);
    }

    private static FixedWindowCounter Build(out TestClock clock, out IMemoryCache cache)
    {
        clock = new TestClock();
        cache = new MemoryCache(new MemoryCacheOptions());

        return new FixedWindowCounter(clock);
    }
}