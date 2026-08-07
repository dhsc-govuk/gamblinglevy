using Domain.Users;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Unit.Users;

/// <summary>
/// BLUM-7239 AC3 — the persistent lockout counter on user_mfas.
///
/// This is the control of record for "too many incorrect codes". The in-memory damper in front of it resets
/// whenever the App Service unloads, which under always_on = false is roughly every twenty minutes idle;
/// this counter is a column, so it survives that and would survive a scale-out.
/// </summary>
public class UserMfaLockoutTests
{
    [Fact]
    public void ShouldApplyTheLock_OnTheConfiguredAttempt()
    {
        var mfa = Enabled();

        mfa.RecordFailedAttempt(maxAttempts: 3, lockoutMinutes: 30).Should().BeFalse();
        mfa.RecordFailedAttempt(maxAttempts: 3, lockoutMinutes: 30).Should().BeFalse();
        mfa.RecordFailedAttempt(maxAttempts: 3, lockoutMinutes: 30).Should().BeTrue();

        mfa.IsLockedOut().Should().BeTrue();
        mfa.FailedAttempts.Should().Be(3);
    }

    [Fact]
    public void ShouldReportTheLock_OnlyOnTheAttemptThatAppliedIt()
    {
        // The return value drives session revocation and the mfa.locked_out audit row. Reporting true again
        // on every later guess would revoke repeatedly and write a row per attempt — handing the attacker
        // control of how many rows the service writes.
        var mfa = Enabled();

        mfa.RecordFailedAttempt(maxAttempts: 2, lockoutMinutes: 30);
        mfa.RecordFailedAttempt(maxAttempts: 2, lockoutMinutes: 30).Should().BeTrue();

        var lockedUntil = mfa.LockedUntil;

        mfa.RecordFailedAttempt(maxAttempts: 2, lockoutMinutes: 30).Should().BeFalse();
        mfa.RecordFailedAttempt(maxAttempts: 2, lockoutMinutes: 30).Should().BeFalse();

        mfa.LockedUntil.Should().Be(
            lockedUntil,
            "extending the lock on every guess made during it would let an attacker hold a victim out indefinitely");
    }

    [Fact]
    public void ShouldStartAFreshWindow_OnceAnExpiredLockIsSeen()
    {
        // The single most important case in this file. Before BLUM-7239 the counter was never cleared, so it
        // stayed at or above the threshold forever and the first wrong digit after a lock expired re-locked
        // for another full period — a permanent lockout. It was not even escapable by the route designed for
        // it, because recovery-code verification consults the same lock.
        //
        // lockoutMinutes: 0 makes the lock elapse the instant it is applied, which is the only way to observe
        // the reset without sleeping through a real one.
        var mfa = Enabled();

        mfa.RecordFailedAttempt(maxAttempts: 2, lockoutMinutes: 0);
        mfa.RecordFailedAttempt(maxAttempts: 2, lockoutMinutes: 0).Should().BeTrue();
        mfa.FailedAttempts.Should().Be(2);
        mfa.IsLockedOut().Should().BeFalse("the lock has already elapsed");

        var afterExpiry = mfa.RecordFailedAttempt(maxAttempts: 2, lockoutMinutes: 0);

        afterExpiry.Should().BeFalse("one wrong code after a lock expires must not re-lock");
        mfa.FailedAttempts.Should().Be(1, "the counter restarted with the window rather than carrying on from 2");
    }

    [Fact]
    public void ShouldNeverLock_WhenTheThresholdIsZero()
    {
        var mfa = Enabled();

        for (var i = 0; i < 10; i++)
        {
            mfa.RecordFailedAttempt(maxAttempts: 0, lockoutMinutes: 30).Should().BeFalse();
        }

        mfa.IsLockedOut().Should().BeFalse();
        mfa.FailedAttempts.Should().Be(10, "the attempts are still counted, they simply never lock");
    }

    [Fact]
    public void ShouldClearTheLock_OnASuccessfulVerification()
    {
        var mfa = Enabled();

        mfa.RecordFailedAttempt(maxAttempts: 1, lockoutMinutes: 30).Should().BeTrue();
        mfa.ResetFailedAttempts();

        mfa.FailedAttempts.Should().Be(0);
        mfa.LockedUntil.Should().BeNull();
        mfa.IsLockedOut().Should().BeFalse();
    }

    private static UserMfa Enabled()
    {
        var mfa = UserMfa.New(UserMfaId.New(), UserId.New(), "encrypted-secret");
        mfa.Enable();

        return mfa;
    }
}