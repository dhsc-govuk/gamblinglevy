using Domain.AuditLogs;
using Domain.Users;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Unit.AuditLogs;

/// <summary>BLUM-8750 — the payload version starts at 1 and only goes up.</summary>
public class AuditLogPayloadVersionTests
{
    [Fact]
    public void ShouldDefaultToTheInitialVersion()
    {
        Create().PayloadVersion.Should().Be(AuditLog.InitialPayloadVersion);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ShouldRejectAVersionBelowTheInitialOne(int payloadVersion)
    {
        var act = () => Create(payloadVersion);

        act.Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("payloadVersion");
    }

    private static AuditLog Create(int payloadVersion = AuditLog.InitialPayloadVersion) =>
        AuditLog.Create(UserId.New(), AuditTargetType.User, Guid.NewGuid(), AuditAction.UserUpdated, before: null, after: null, payloadVersion: payloadVersion);
}