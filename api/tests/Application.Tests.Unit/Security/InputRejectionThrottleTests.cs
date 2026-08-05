using Application.Common.Interfaces;
using Application.Common.Settings;
using FluentAssertions;
using Infrastructure.Security;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace Application.Tests.Unit.Security;

/// <summary>
/// BLUM-7240 AC8 — the cap on how many audit_logs rows a rejected request may cause.
///
/// Tested here rather than through the API because the integration suite switches the caps off: the throttle
/// counts in a process-wide cache that outlives a test, so with the caps live an assertion's result would
/// depend on how many earlier tests happened to send hostile input. Switching a control off globally is not
/// a reason to leave it uncovered, and every case below is one the first implementation got wrong.
/// </summary>
public class InputRejectionThrottleTests
{
    private const string Actor = "user-a_POST /organisations/local-authorities";
    private const string OtherActor = "user-b_POST /activities";

    [Fact]
    public void ShouldAdmitUpToThePerActorCap_AndSuppressBeyondIt()
    {
        var throttle = Build(out _);
        var limits = Limits(perActor: 3, global: 0);

        var decisions = Enumerable.Range(0, 5).Select(_ => throttle.Reserve(Actor, limits)).ToList();

        decisions.Take(3).Should().OnlyContain(d => d.Admitted);
        decisions.Skip(3).Should().OnlyContain(d => !d.Admitted);
    }

    [Fact]
    public void ShouldNotChargeTheGlobalBudgetForASuppressedRejection()
    {
        // The defect this test exists for: the global counter used to be incremented before the per-actor cap
        // could suppress, so one noisy caller spent the shared budget and blanked the trail for everyone else.
        var throttle = Build(out _);
        var limits = Limits(perActor: 1, global: 5);

        for (var i = 0; i < 20; i++)
        {
            throttle.Reserve(Actor, limits);
        }

        // The noisy actor took exactly one row. The remaining four of the global five must still be available
        // to a different caller.
        var other = Enumerable.Range(0, 5).Select(_ => throttle.Reserve(OtherActor, limits)).ToList();

        other[0].Admitted.Should().BeTrue();
        other.Count(d => d.Admitted).Should().Be(1, "the second caller has its own per-actor cap of 1");
    }

    [Fact]
    public void ShouldApplyTheGlobalCapAcrossDistinctActors()
    {
        var throttle = Build(out _);
        var limits = Limits(perActor: 0, global: 3);

        var admitted = Enumerable.Range(0, 10)
            .Select(i => throttle.Reserve($"actor-{i}_POST /x", limits))
            .Count(d => d.Admitted);

        admitted.Should().Be(3, "a probe spread across accounts must not flood the table either");
    }

    [Fact]
    public void ShouldCarrySuppressedCountOntoTheNextAdmittedRow()
    {
        // Suppression degrades a rejection to telemetry rather than dropping it: the burst stays visible in
        // the audit trail as a number.
        var throttle = Build(out var clock);
        var limits = Limits(perActor: 1, global: 0);

        throttle.Reserve(Actor, limits).Admitted.Should().BeTrue();

        for (var i = 0; i < 7; i++)
        {
            throttle.Reserve(Actor, limits).Admitted.Should().BeFalse();
        }

        clock.UtcNow = clock.UtcNow.AddHours(2);

        var next = throttle.Reserve(Actor, limits);
        next.Admitted.Should().BeTrue();
        next.SuppressedSincePrevious.Should().Be(7);
    }

    [Fact]
    public void ShouldResetSuppressedCount_OnceItHasBeenReported()
    {
        var throttle = Build(out var clock);
        var limits = Limits(perActor: 1, global: 0);

        throttle.Reserve(Actor, limits);
        throttle.Reserve(Actor, limits);

        clock.UtcNow = clock.UtcNow.AddHours(2);
        throttle.Reserve(Actor, limits).SuppressedSincePrevious.Should().Be(1);

        clock.UtcNow = clock.UtcNow.AddHours(2);
        throttle.Reserve(Actor, limits).SuppressedSincePrevious.Should().Be(0);
    }

    [Fact]
    public void ShouldUseAFixedWindow_NotASlidingOne()
    {
        // A window that restarted on every increment would keep a caller suppressed indefinitely as long as
        // they kept trying — the counter would never age out.
        var throttle = Build(out var clock);
        var limits = Limits(perActor: 1, global: 0);

        throttle.Reserve(Actor, limits).Admitted.Should().BeTrue();

        for (var i = 0; i < 5; i++)
        {
            clock.UtcNow = clock.UtcNow.AddMinutes(10);
            throttle.Reserve(Actor, limits).Admitted.Should().BeFalse();
        }

        // Past the original hour, counted from the first rejection rather than the last.
        clock.UtcNow = clock.UtcNow.AddMinutes(20);

        throttle.Reserve(Actor, limits).Admitted.Should().BeTrue();
    }

    [Theory]
    [InlineData(0, 0, "both unlimited")]
    [InlineData(0, 5, "per-actor unlimited, global not reached")]
    [InlineData(5, 0, "global unlimited, per-actor not reached")]
    public void ShouldTreatZeroAsUnlimited(int perActor, int global, string why)
    {
        why.Should().NotBeEmpty();

        var throttle = Build(out _);

        var admitted = Enumerable.Range(0, 4).Count(_ => throttle.Reserve(Actor, Limits(perActor, global)).Admitted);

        admitted.Should().Be(4);
    }

    [Fact]
    public void ShouldNotLoseIncrementsUnderConcurrency()
    {
        // Counters were previously mutable objects handed out by IMemoryCache.GetOrCreate and incremented
        // without synchronisation, so concurrent misses each built their own and one increment was discarded
        // — the cap the setting promised was not the cap being applied.
        var throttle = Build(out _);
        var limits = Limits(perActor: 50, global: 0);

        var admitted = 0;
        Parallel.For(0, 500, _ =>
        {
            if (throttle.Reserve(Actor, limits).Admitted)
            {
                Interlocked.Increment(ref admitted);
            }
        });

        admitted.Should().Be(50);
    }

    private static InputValidationSettings Limits(int perActor, int global)
    {
        return new InputValidationSettings
        {
            AuditSecurityRejections = true,
            MaxAuditRowsPerActorPerHour = perActor,
            MaxAuditRowsPerHour = global,
        };
    }

    /// <summary>
    /// The pinned instant is deliberately in the past and must stay there: it is what guards the throttle
    /// against handing IMemoryCache a real-clock deadline derived from the injected clock. The cache judges
    /// an absolute expiry against the system clock and declines to store an entry that has already expired,
    /// so a throttle written that way stores nothing, reads every counter back as zero and admits everything
    /// — which is exactly how this whole class once failed, silently, the day after it was written.
    /// </summary>
    private static InputRejectionThrottle Build(out FixedClock clock)
    {
        clock = new FixedClock { UtcNow = new DateTime(2026, 8, 4, 9, 0, 0, DateTimeKind.Utc) };
        return new InputRejectionThrottle(new MemoryCache(new MemoryCacheOptions()), clock);
    }

    private sealed class FixedClock : IDateTimeProvider
    {
        public DateTime UtcNow { get; set; }

        public DateTime GetNow() => UtcNow;

        public DateTime GetUtcNow() => UtcNow;
    }
}