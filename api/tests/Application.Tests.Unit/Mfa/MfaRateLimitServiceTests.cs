using Application.Common.Settings;
using Application.Tests.Unit.Security;
using Domain.Users;
using FluentAssertions;
using Infrastructure.Mfa;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace Application.Tests.Unit.Mfa;

/// <summary>
/// BLUM-7239 AC3 — the per-user damper in front of the persistent MFA lockout.
///
/// It had no test at all before this, which is how it kept three defects that each made the configured cap
/// not the cap actually applied. Every case below is one of them, or one of the behaviours that had to be
/// preserved while fixing them.
/// </summary>
public class MfaRateLimitServiceTests
{
    private static readonly UserId User = UserId.New();
    private static readonly UserId OtherUser = UserId.New();

    [Fact]
    public void ShouldAllowUpToTheLimit_AndRefuseTheNext()
    {
        var service = Build(out _, limit: 3);

        Enumerable.Range(0, 3).Should().OnlyContain(_ => service.Reserve(User).Allowed);

        var refused = service.Reserve(User);

        refused.Allowed.Should().BeFalse();
        refused.RetryAfterSeconds.Should().BeGreaterThan(0, "a Retry-After of zero is a busy loop");
    }

    [Fact]
    public void ShouldCountAttempts_NotOnlyFailures()
    {
        // Deliberate, and the behaviour that existed before: the damper is on guessing, so a correct code
        // arriving after the budget is spent is still refused. ResetAttempts on success is what stops this
        // affecting somebody who simply mistyped.
        var service = Build(out _, limit: 2);

        service.Reserve(User).Allowed.Should().BeTrue();
        service.Reserve(User).Allowed.Should().BeTrue();
        service.Reserve(User).Allowed.Should().BeFalse();
    }

    [Fact]
    public void ShouldGiveAFreshBudget_AfterASuccess()
    {
        var service = Build(out _, limit: 2);

        service.Reserve(User);
        service.Reserve(User);
        service.ResetAttempts(User);

        service.Reserve(User).Allowed.Should().BeTrue();
    }

    [Fact]
    public void ShouldKeepUsersIndependent()
    {
        var service = Build(out _, limit: 1);

        service.Reserve(User).Allowed.Should().BeTrue();
        service.Reserve(User).Allowed.Should().BeFalse();

        service.Reserve(OtherUser).Allowed.Should().BeTrue("one user's guessing must not lock out another");
    }

    [Fact]
    public void ShouldUseAFixedWindow_NotASlidingOne()
    {
        // The defect: the expiry was re-armed on every write, so somebody trickling one attempt just inside
        // each window stayed refused forever while the counter never aged out.
        var service = Build(out var clock, limit: 2, windowSeconds: 60);

        service.Reserve(User);

        for (var i = 0; i < 4; i++)
        {
            clock.UtcNow = clock.UtcNow.AddSeconds(15);
            service.Reserve(User);
        }

        clock.UtcNow = clock.UtcNow.AddSeconds(5);

        service.Reserve(User).Allowed.Should().BeTrue("the window ran from the first attempt, not the last");
    }

    [Fact]
    public void ShouldTreatZeroAsUnlimited()
    {
        // Not a nicety: a damper and a lockout that both trip at five would mean the damper always answered
        // first, so the lockout could never be reached and MfaLockoutTests could not exist.
        var service = Build(out _, limit: 0);

        Enumerable.Range(0, 50).Should().OnlyContain(_ => service.Reserve(User).Allowed);
    }

    [Fact]
    public void ShouldNotLoseAttemptsUnderConcurrency()
    {
        // The defect: IMemoryCache.GetOrCreate handed out a mutable object which was incremented without
        // synchronisation, so two concurrent attempts each built their own and one was discarded.
        var service = Build(out _, limit: 50);

        var allowed = 0;
        Parallel.For(0, 400, _ =>
        {
            if (service.Reserve(User).Allowed)
            {
                Interlocked.Increment(ref allowed);
            }
        });

        allowed.Should().Be(50);
    }

    private static MfaRateLimitService Build(out TestClock clock, int limit, int windowSeconds = 60)
    {
        clock = new TestClock();

        var settings = new ApplicationSettings();
        settings.Mfa.MaxAttemptsPerWindow = limit;
        settings.Mfa.RateLimitWindowSeconds = windowSeconds;

        return new MfaRateLimitService(new MemoryCache(new MemoryCacheOptions()), clock, settings);
    }
}