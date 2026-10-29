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
    private const int LockoutMinutes = 30;

    // The lock is judged against the instant the caller hands in (BLUM-8753), so every test states when it is
    // rather than depending on when the suite runs — and can step past a lockout without sleeping through it.
    private static readonly DateTime Now = new(2026, 8, 4, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ShouldApplyTheLock_OnTheConfiguredAttempt()
    {
        var mfa = Enabled();

        mfa.RecordFailedAttempt(maxAttempts: 3, LockoutMinutes, Now).Should().BeFalse();
        mfa.RecordFailedAttempt(maxAttempts: 3, LockoutMinutes, Now).Should().BeFalse();
        mfa.RecordFailedAttempt(maxAttempts: 3, LockoutMinutes, Now).Should().BeTrue();

        mfa.IsLockedOut(Now).Should().BeTrue();
        mfa.FailedAttempts.Should().Be(3);
        mfa.LockedUntil.Should().Be(Now.AddMinutes(LockoutMinutes));
    }

    [Fact]
    public void ShouldReportTheLock_OnlyOnTheAttemptThatAppliedIt()
    {
        // The return value drives session revocation and the mfa.locked_out audit row. Reporting true again
        // on every later guess would revoke repeatedly and write a row per attempt — handing the attacker
        // control of how many rows the service writes.
        var mfa = Enabled();

        mfa.RecordFailedAttempt(maxAttempts: 2, LockoutMinutes, Now);
        mfa.RecordFailedAttempt(maxAttempts: 2, LockoutMinutes, Now).Should().BeTrue();

        var lockedUntil = mfa.LockedUntil;

        mfa.RecordFailedAttempt(maxAttempts: 2, LockoutMinutes, Now.AddMinutes(1)).Should().BeFalse();
        mfa.RecordFailedAttempt(maxAttempts: 2, LockoutMinutes, Now.AddMinutes(29)).Should().BeFalse();

        mfa.LockedUntil.Should().Be(
            lockedUntil,
            "extending the lock on every guess made during it would let an attacker hold a victim out indefinitely");
    }

    [Fact]
    public void ShouldHoldTheLock_UntilTheLockoutPeriodHasRun()
    {
        var mfa = Enabled();

        mfa.RecordFailedAttempt(maxAttempts: 1, LockoutMinutes, Now).Should().BeTrue();

        var lockedUntil = Now.AddMinutes(LockoutMinutes);

        mfa.IsLockedOut(lockedUntil.AddSeconds(-1)).Should().BeTrue();
        mfa.IsLockedOut(lockedUntil).Should().BeFalse("the lock ends at LockedUntil, not after it");
    }

    [Fact]
    public void ShouldStartAFreshWindow_OnceAnExpiredLockIsSeen()
    {
        // The single most important case in this file. Before BLUM-7239 the counter was never cleared, so it
        // stayed at or above the threshold forever and the first wrong digit after a lock expired re-locked
        // for another full period — a permanent lockout. It was not even escapable by the route designed for
        // it, because recovery-code verification consults the same lock.
        var mfa = Enabled();

        mfa.RecordFailedAttempt(maxAttempts: 2, LockoutMinutes, Now);
        mfa.RecordFailedAttempt(maxAttempts: 2, LockoutMinutes, Now).Should().BeTrue();
        mfa.FailedAttempts.Should().Be(2);

        var afterExpiry = mfa.RecordFailedAttempt(maxAttempts: 2, LockoutMinutes, Now.AddMinutes(LockoutMinutes));

        afterExpiry.Should().BeFalse("one wrong code after a lock expires must not re-lock");
        mfa.FailedAttempts.Should().Be(1, "the counter restarted with the window rather than carrying on from 2");
        mfa.LockedUntil.Should().BeNull();
    }

    [Fact]
    public void ShouldKeepCounting_WhileTheLockIsStillLive()
    {
        // The other side of the case above: one second short of expiry the window has not ended, so the
        // counter must not restart — otherwise a guess timed just before expiry would wipe the record of it.
        var mfa = Enabled();

        mfa.RecordFailedAttempt(maxAttempts: 2, LockoutMinutes, Now);
        mfa.RecordFailedAttempt(maxAttempts: 2, LockoutMinutes, Now).Should().BeTrue();

        var justBeforeExpiry = Now.AddMinutes(LockoutMinutes).AddSeconds(-1);

        mfa.RecordFailedAttempt(maxAttempts: 2, LockoutMinutes, justBeforeExpiry).Should().BeFalse();

        mfa.FailedAttempts.Should().Be(3);
        mfa.IsLockedOut(justBeforeExpiry).Should().BeTrue();
    }

    [Fact]
    public void ShouldNeverLock_WhenTheThresholdIsZero()
    {
        var mfa = Enabled();

        for (var i = 0; i < 10; i++)
        {
            mfa.RecordFailedAttempt(maxAttempts: 0, LockoutMinutes, Now).Should().BeFalse();
        }

        mfa.IsLockedOut(Now).Should().BeFalse();
        mfa.FailedAttempts.Should().Be(10, "the attempts are still counted, they simply never lock");
    }

    [Fact]
    public void ShouldClearTheLock_OnASuccessfulVerification()
    {
        var mfa = Enabled();

        mfa.RecordFailedAttempt(maxAttempts: 1, LockoutMinutes, Now).Should().BeTrue();
        mfa.ResetFailedAttempts(Now);

        mfa.FailedAttempts.Should().Be(0);
        mfa.LockedUntil.Should().BeNull();
        mfa.IsLockedOut(Now).Should().BeFalse();
    }

    private static UserMfa Enabled()
    {
        var mfa = UserMfa.New(UserMfaId.New(), UserId.New(), "encrypted-secret", Now);
        mfa.Enable(Now);

        return mfa;
    }
}