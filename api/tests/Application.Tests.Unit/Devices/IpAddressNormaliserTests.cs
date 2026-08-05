using Application.Common.Devices;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Unit.Devices;

/// <summary>
/// BLUM-7241 — the client IP comes from a proxy header any caller can set to any string, and is persisted in
/// a varchar(45) column. Anything that gets through here and is not a real address is either a failed INSERT
/// that blocks sign-in, or a lie shown to the user on their own sessions list.
/// </summary>
public class IpAddressNormaliserTests
{
    [Theory]
    [InlineData("203.0.113.10", "203.0.113.10")]
    [InlineData("  203.0.113.10  ", "203.0.113.10")]
    [InlineData("2001:db8::1", "2001:db8::1")]
    public void ShouldKeepAnAddressThatIsAlreadyClean(string candidate, string expected)
    {
        IpAddressNormaliser.Normalise(candidate).Should().Be(expected);
    }

    [Theory]
    // A forwarding chain names the original client first.
    [InlineData("203.0.113.10, 198.51.100.7, 192.0.2.1", "203.0.113.10")]
    [InlineData("203.0.113.10:41237", "203.0.113.10")]
    [InlineData("[2001:db8::1]:41237", "2001:db8::1")]
    [InlineData("[2001:db8::1]", "2001:db8::1")]
    public void ShouldUnwrapTheFormsProxiesActuallySend(string candidate, string expected)
    {
        IpAddressNormaliser.Normalise(candidate).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-address")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("203.0.113.999")]
    public void ShouldFallBackToUnknown_WhenItIsNotAnAddress(string? candidate)
    {
        IpAddressNormaliser.Normalise(candidate).Should().Be(IpAddressNormaliser.Unknown);
    }

    [Fact]
    public void ShouldRejectAnOverLongHeader_RatherThanLetItReachTheColumn()
    {
        // The finding this test exists for: ip_address is varchar(45), so a 60-character header used to make
        // the session INSERT throw 22001 and fail the whole MFA verification. Anyone could send it.
        var overLong = new string('a', 60);

        var result = IpAddressNormaliser.Normalise(overLong);

        result.Should().Be(IpAddressNormaliser.Unknown);
        result.Length.Should().BeLessThanOrEqualTo(45);
    }

    [Fact]
    public void ShouldNeverExceedTheStoredColumnWidth()
    {
        // The longest legitimate input there is: a full IPv6 literal with an embedded IPv4 tail.
        var longest = IpAddressNormaliser.Normalise("ffff:ffff:ffff:ffff:ffff:ffff:255.255.255.255");

        longest.Length.Should().BeLessThanOrEqualTo(45);
        longest.Should().NotBe(IpAddressNormaliser.Unknown);
    }
}