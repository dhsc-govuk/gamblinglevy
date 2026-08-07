using FluentAssertions;
using Infrastructure.Security;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace Application.Tests.Unit.Security;

/// <summary>
/// BLUM-7239 — the audit-flood cap, generalised out of InputRejectionThrottle so a second kind of security
/// event could reuse it.
///
/// The per-actor, global, suppressed-carry and fixed-window behaviours are already pinned by
/// InputRejectionThrottleTests, which passes against the extraction unedited. What is tested here is the
/// risk the extraction itself introduced: scope.
/// </summary>
public class AuditFloodThrottleTests
{
    private const string RateLimitScope = "rate_limit_audit";
    private const string InputScope = "input_rejection_audit";
    private const string Actor = "203.0.113.1_POST /organisations/local-authorities";

    private static readonly TimeSpan Window = TimeSpan.FromHours(1);

    [Fact]
    public void ShouldKeepGlobalBudgetsSeparatePerScope()
    {
        // The defect this test exists for. A single shared global key would mean a rate-limit flood — the
        // loudest thing this service can experience — silently exhausting the budget that carries the
        // input-validation trail, blanking a CWE-20 audit trail via a CWE-770 attack.
        var throttle = Build(out _);
        var limits = Limits(perActor: 0, global: 3);

        var rateLimitAdmitted = Enumerable.Range(0, 10)
            .Count(i => throttle.Reserve(RateLimitScope, $"actor-{i}", limits).Admitted);

        var inputAdmitted = Enumerable.Range(0, 10)
            .Count(i => throttle.Reserve(InputScope, $"actor-{i}", limits).Admitted);

        rateLimitAdmitted.Should().Be(3);
        inputAdmitted.Should().Be(3, "the second scope has its own global budget, untouched by the first");
    }

    [Fact]
    public void ShouldKeepPerActorBudgetsSeparatePerScope()
    {
        var throttle = Build(out _);
        var limits = Limits(perActor: 1, global: 0);

        throttle.Reserve(RateLimitScope, Actor, limits).Admitted.Should().BeTrue();
        throttle.Reserve(RateLimitScope, Actor, limits).Admitted.Should().BeFalse();

        throttle.Reserve(InputScope, Actor, limits).Admitted
            .Should().BeTrue("the same actor is counted once per kind of event, not once overall");
    }

    [Fact]
    public void ShouldNotLetOneActorsSuppressionBlankAnothersTrail()
    {
        var throttle = Build(out _);
        var limits = Limits(perActor: 1, global: 5);

        for (var i = 0; i < 20; i++)
        {
            throttle.Reserve(RateLimitScope, Actor, limits);
        }

        throttle.Reserve(RateLimitScope, "203.0.113.9_GET /activities", limits).Admitted
            .Should().BeTrue("only the one admitted row was charged to the global budget");
    }

    [Fact]
    public void ShouldCarrySuppressedCountOntoTheNextAdmittedRow()
    {
        var throttle = Build(out var clock);
        var limits = Limits(perActor: 1, global: 0);

        throttle.Reserve(RateLimitScope, Actor, limits).Admitted.Should().BeTrue();

        for (var i = 0; i < 7; i++)
        {
            throttle.Reserve(RateLimitScope, Actor, limits).Admitted.Should().BeFalse();
        }

        clock.UtcNow = clock.UtcNow.AddHours(2);

        var next = throttle.Reserve(RateLimitScope, Actor, limits);

        next.Admitted.Should().BeTrue();
        next.SuppressedSincePrevious.Should().Be(7, "the burst stays visible as a number rather than vanishing");
    }

    [Fact]
    public void ShouldHonourTheWindowItIsGiven()
    {
        // The window is a parameter now rather than a hardcoded hour, because the two callers do not agree
        // on one. This pins that it is actually used.
        var throttle = Build(out var clock);
        var limits = new AuditThrottleLimits(1, 0, TimeSpan.FromMinutes(5));

        throttle.Reserve(RateLimitScope, Actor, limits).Admitted.Should().BeTrue();
        throttle.Reserve(RateLimitScope, Actor, limits).Admitted.Should().BeFalse();

        clock.UtcNow = clock.UtcNow.AddMinutes(6);

        throttle.Reserve(RateLimitScope, Actor, limits).Admitted.Should().BeTrue();
    }

    private static AuditThrottleLimits Limits(int perActor, int global)
    {
        return new AuditThrottleLimits(perActor, global, Window);
    }

    private static AuditFloodThrottle Build(out TestClock clock)
    {
        clock = new TestClock();

        return new AuditFloodThrottle(new MemoryCache(new MemoryCacheOptions()), new FixedWindowCounter(clock));
    }
}