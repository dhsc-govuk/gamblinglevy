using Domain.Users;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Unit.Users;

/// <summary>
/// BLUM-7238 / CWE-613 — the idle and absolute windows that bound a signed-in session. This is the whole of
/// the decision; MfaSessionService only acts on the verdict, so the boundaries are pinned here rather than
/// in an integration test that would need a container to assert the same arithmetic.
///
/// Every "now" below is derived from the session's own CreatedAt. MfaSession.New stamps CreatedAt,
/// LastActivityAt and ExpiresAt from the one instant it is handed, and that instant is fixed here, so
/// these assertions are deterministic whatever time of day the suite runs.
/// </summary>
public class MfaSessionTests
{
    private const string IpHash = "ip-hash";
    private const string UserAgentHash = "user-agent-hash";
    private const int IdleTimeoutMinutes = 30;
    private const int ExpiryHours = 8;

    private static readonly DateTime CreatedAt = new(2026, 8, 4, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void New_StartsTheIdleWindowAtCreation()
    {
        var session = NewSession();

        session.LastActivityAt.Should().Be(session.CreatedAt);
        session.ExpiresAt.Should().Be(session.CreatedAt.AddHours(ExpiryHours));
    }

    [Fact]
    public void Evaluate_IsValid_WhenInsideBothWindows()
    {
        var session = NewSession();

        var status = Evaluate(session, session.CreatedAt.AddMinutes(29));

        status.Should().Be(MfaSessionStatus.Valid);
    }

    [Fact]
    public void Evaluate_IsValid_AtExactlyTheIdleBoundary()
    {
        // The rule is "idle for longer than the timeout", so landing precisely on it is still in time.
        // Asserted explicitly because an off-by-one here signs people out a request early.
        var session = NewSession();

        var status = Evaluate(session, session.CreatedAt.AddMinutes(IdleTimeoutMinutes));

        status.Should().Be(MfaSessionStatus.Valid);
    }

    [Fact]
    public void Evaluate_IsIdleTimeout_WhenIdleLongerThanTheWindow()
    {
        var session = NewSession();

        var status = Evaluate(session, session.CreatedAt.AddMinutes(IdleTimeoutMinutes).AddSeconds(1));

        status.Should().Be(MfaSessionStatus.IdleTimeout);
    }

    [Fact]
    public void Evaluate_IsExpired_ForAContinuouslyActiveSessionAtTheAbsoluteCap()
    {
        // AC2: this user never goes idle, so only the absolute cap can end their session.
        var session = NewSession();
        session.RecordActivity(session.ExpiresAt);

        var status = Evaluate(session, session.ExpiresAt.AddSeconds(1));

        status.Should().Be(MfaSessionStatus.Expired);
    }

    [Fact]
    public void Evaluate_IsValid_AtExactlyTheAbsoluteBoundary()
    {
        var session = NewSession();
        session.RecordActivity(session.ExpiresAt);

        var status = Evaluate(session, session.ExpiresAt);

        status.Should().Be(MfaSessionStatus.Valid);
    }

    [Fact]
    public void Evaluate_PrefersExpired_OverIdleTimeout()
    {
        var session = NewSession();

        var status = Evaluate(session, session.ExpiresAt.AddHours(1));

        status.Should().Be(MfaSessionStatus.Expired);
    }

    [Fact]
    public void Evaluate_IsRevoked_EvenWhenBothWindowsAreOpen()
    {
        var session = NewSession();
        session.Revoke();

        var status = Evaluate(session, session.CreatedAt.AddMinutes(1));

        status.Should().Be(MfaSessionStatus.Revoked);
    }

    [Theory]
    [InlineData("other-ip-hash", UserAgentHash)]
    [InlineData(IpHash, "other-user-agent-hash")]
    [InlineData("other-ip-hash", "other-user-agent-hash")]
    public void Evaluate_IsDeviceMismatch_WhenTheBindingDoesNotMatch_AndIpBindingIsOn(string ipHash, string userAgentHash)
    {
        var session = NewSession();

        var status = session.Evaluate(
            ipHash,
            userAgentHash,
            session.CreatedAt.AddMinutes(1),
            IdleTimeoutMinutes,
            bindToIpAddress: true);

        status.Should().Be(MfaSessionStatus.DeviceMismatch);
    }

    [Theory]
    [InlineData(IpHash, "other-user-agent-hash")]
    [InlineData("other-ip-hash", "other-user-agent-hash")]
    public void Evaluate_IsDeviceMismatch_WhenTheUserAgentChanges_AndIpBindingIsOff(string ipHash, string userAgentHash)
    {
        var session = NewSession();

        var status = session.Evaluate(
            ipHash,
            userAgentHash,
            session.CreatedAt.AddMinutes(1),
            IdleTimeoutMinutes,
            bindToIpAddress: false);

        status.Should().Be(MfaSessionStatus.DeviceMismatch);
    }

    [Fact]
    public void Evaluate_IsValid_WhenOnlyTheIpChanges_AndIpBindingIsOff()
    {
        // BLUM-7848 — proxy pools and edge connection recycling move the address of a user who has not moved.
        var session = NewSession();

        var status = session.Evaluate(
            "other-ip-hash",
            UserAgentHash,
            session.CreatedAt.AddMinutes(1),
            IdleTimeoutMinutes,
            bindToIpAddress: false);

        status.Should().Be(MfaSessionStatus.Valid);
    }

    [Fact]
    public void Evaluate_ReportsTheTimeout_AheadOfTheDeviceBinding()
    {
        // A replayed cookie on an aged-out session must still be reported as a timeout, because that verdict
        // is what makes the caller destroy the record. Answering DeviceMismatch instead would leave the row
        // alive for whoever presents the matching device next.
        var session = NewSession();

        var status = session.Evaluate(
            "other-ip-hash",
            "other-user-agent-hash",
            session.CreatedAt.AddMinutes(45),
            IdleTimeoutMinutes,
            bindToIpAddress: true);

        status.Should().Be(MfaSessionStatus.IdleTimeout);
    }

    [Fact]
    public void RecordActivity_SlidesTheIdleWindowWithoutMovingTheAbsoluteCap()
    {
        var session = NewSession();
        var expiresAt = session.ExpiresAt;

        session.RecordActivity(session.CreatedAt.AddMinutes(25));

        session.LastActivityAt.Should().Be(session.CreatedAt.AddMinutes(25));
        session.ExpiresAt.Should().Be(expiresAt);
        Evaluate(session, session.CreatedAt.AddMinutes(50)).Should().Be(MfaSessionStatus.Valid);
    }

    private static MfaSessionStatus Evaluate(MfaSession session, DateTime utcNow) =>
        session.Evaluate(IpHash, UserAgentHash, utcNow, IdleTimeoutMinutes, bindToIpAddress: true);

    private static MfaSession NewSession() =>
        MfaSession.New(MfaSessionId.New(), UserId.New(), IpHash, UserAgentHash, "198.51.100.7", "Chrome on Windows", CreatedAt, ExpiryHours);
}