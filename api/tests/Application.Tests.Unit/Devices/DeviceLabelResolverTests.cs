using Application.Common.Devices;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Unit.Devices;

/// <summary>
/// BLUM-7241 — the label a user reads when deciding whether a session is theirs. It is a recognition aid,
/// never an authorisation input, so the bar is "would the owner of this device recognise it", not exhaustive
/// User-Agent coverage.
/// </summary>
public class DeviceLabelResolverTests
{
    [Theory]
    [InlineData(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36",
        "Chrome on Windows")]
    [InlineData(
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36",
        "Chrome on macOS")]
    [InlineData(
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.4 Safari/605.1.15",
        "Safari on macOS")]
    [InlineData(
        "Mozilla/5.0 (iPhone; CPU iPhone OS 17_4 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.4 Mobile/15E148 Safari/604.1",
        "Safari on iPhone")]
    [InlineData(
        "Mozilla/5.0 (iPad; CPU OS 17_4 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.4 Mobile/15E148 Safari/604.1",
        "Safari on iPad")]
    [InlineData(
        "Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Mobile Safari/537.36",
        "Chrome on Android")]
    [InlineData(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:125.0) Gecko/20100101 Firefox/125.0",
        "Firefox on Windows")]
    [InlineData(
        "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36",
        "Chrome on Linux")]
    public void ShouldDescribeCommonBrowsersAndPlatforms(string userAgent, string expected)
    {
        DeviceLabelResolver.Resolve(userAgent).Should().Be(expected);
    }

    [Theory]
    // Every Chromium browser also claims to be Chrome, and Chrome itself also claims to be Safari, so the
    // more specific token has to win or half the estate reports as something it is not.
    [InlineData(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36 Edg/124.0.0.0",
        "Edge on Windows")]
    [InlineData(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36 OPR/110.0.0.0",
        "Opera on Windows")]
    [InlineData(
        "Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 (KHTML, like Gecko) SamsungBrowser/25.0 Chrome/121.0.0.0 Mobile Safari/537.36",
        "Samsung Internet on Android")]
    public void ShouldPreferTheMoreSpecificBrowser_WhenOneImpersonatesAnother(string userAgent, string expected)
    {
        DeviceLabelResolver.Resolve(userAgent).Should().Be(expected);
    }

    [Theory]
    [InlineData("curl/8.4.0", "Unknown device")]
    [InlineData("", "Unknown device")]
    [InlineData("   ", "Unknown device")]
    [InlineData(null, "Unknown device")]
    public void ShouldFallBackToUnknown_WhenNothingIsRecognised(string? userAgent, string expected)
    {
        // A header this service does not recognise must still produce something displayable — the column is
        // NOT NULL and an empty label would render as a blank cell the user cannot act on.
        DeviceLabelResolver.Resolve(userAgent).Should().Be(expected);
    }

    [Fact]
    public void ShouldNameThePlatformAlone_WhenTheBrowserIsUnrecognised()
    {
        DeviceLabelResolver.Resolve("SomeBot/2.1 (Windows NT 10.0)").Should().Be("Windows");
    }

    [Fact]
    public void ShouldNeverExceedTheStoredColumnWidth()
    {
        // device_label is varchar(100); a label that overflowed it would fail the insert and take the whole
        // sign-in down with it.
        var longest = "Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 (KHTML, like Gecko) SamsungBrowser/25.0 Chrome/121.0.0.0 Mobile Safari/537.36";

        DeviceLabelResolver.Resolve(longest).Length.Should().BeLessThanOrEqualTo(100);
    }
}