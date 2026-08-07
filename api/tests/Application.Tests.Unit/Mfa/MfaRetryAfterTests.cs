using Application.Mfa;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Unit.Mfa;

/// <summary>BLUM-7239 — the number a refused MFA attempt puts in Retry-After.</summary>
public class MfaRetryAfterTests
{
    private static readonly DateTime Now = new(2026, 8, 5, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ShouldReportTheSecondsRemaining()
    {
        MfaRetryAfter.Seconds(Now.AddMinutes(10), Now, ceilingSeconds: 1800).Should().Be(600);
    }

    [Fact]
    public void ShouldRoundAPartialSecondUp()
    {
        // Rounding down would advertise the window as over while it is still closed, so a client that
        // believed the header would be refused again the moment it retried.
        MfaRetryAfter.Seconds(Now.AddMilliseconds(41_200), Now, ceilingSeconds: 1800).Should().Be(42);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-60)]
    [InlineData(1)]
    public void ShouldNeverReportZero(int offsetSeconds)
    {
        // A Retry-After of 0 tells a conforming client to come straight back, turning a control meant to slow
        // an attacker down into a busy loop.
        MfaRetryAfter.Seconds(Now.AddSeconds(offsetSeconds), Now, ceilingSeconds: 1800).Should().Be(1);
    }

    [Fact]
    public void ShouldNeverExceedTheCeiling()
    {
        // A clock skew or a stale row must not be able to tell a user to wait longer than the control that
        // produced the number would actually hold them.
        MfaRetryAfter.Seconds(Now.AddDays(3), Now, ceilingSeconds: 1800).Should().Be(1800);
    }

    [Fact]
    public void ShouldAnswerASecond_WhenThereIsNoDeadline()
    {
        MfaRetryAfter.Seconds(until: null, Now, ceilingSeconds: 1800).Should().Be(1);
    }

    [Fact]
    public void ShouldTolerateANonsenseCeiling()
    {
        MfaRetryAfter.Seconds(Now.AddMinutes(10), Now, ceilingSeconds: 0).Should().Be(1);
    }
}