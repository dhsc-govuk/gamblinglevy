using Application.Mfa;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Unit.Mfa;

/// <summary>
/// BLUM-7240 — the accepted shapes for a TOTP code and a recovery code.
///
/// These are checked inside the handlers rather than in a validator so a malformed code still counts as a
/// failed attempt; see MfaCodeFormat for the argument. What matters here is that the shapes are exactly the
/// ones the services can actually verify.
/// </summary>
public class MfaCodeFormatTests
{
    [Theory]
    [InlineData("000000")]
    [InlineData("123456")]
    [InlineData("999999")]
    public void ShouldAcceptASixDigitTotpCode(string code)
    {
        MfaCodeFormat.IsTotpShape(code).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12345a")]
    [InlineData("123 45")]
    [InlineData("12-345")]
    public void ShouldRejectAnythingOutsideTheTotpSpace(string? code)
    {
        MfaCodeFormat.IsTotpShape(code).Should().BeFalse();
    }

    [Fact]
    public void ShouldRejectNonAsciiDigits()
    {
        // The reason the check is written as an explicit range rather than char.IsDigit, which is true of
        // every Unicode decimal digit — Arabic-Indic numerals would pass a check that calls itself an
        // allow-list and then fail verification anyway.
        var arabicIndic = new string([.. "٠١٢٣٤٥"]);

        arabicIndic.Should().HaveLength(6);
        MfaCodeFormat.IsTotpShape(arabicIndic).Should().BeFalse();
    }

    [Theory]
    [InlineData("ABCD-EFGH-JKLM")]
    [InlineData("ABCDEFGHJKLM")]
    [InlineData("abcd-efgh-jklm")]
    [InlineData("2345-6789-ABCD")]
    public void ShouldAcceptWhatTheRecoveryCodeVerifierCanNormalise(string code)
    {
        // VerifyRecoveryCode strips hyphens and upper-cases before comparing, so hyphen placement is not
        // part of the shape and case is not either.
        MfaCodeFormat.IsRecoveryCodeShape(code).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ABCD-EFGH-JKL")]
    [InlineData("ABCD-EFGH-JKLMN")]
    [InlineData("ABCD-EFGH-JKL!")]
    [InlineData("ABCD EFGH JKLM")]
    public void ShouldRejectARecoveryCodeOfTheWrongShape(string? code)
    {
        MfaCodeFormat.IsRecoveryCodeShape(code).Should().BeFalse();
    }

    [Fact]
    public void ShouldRejectAnOverLongCodeWithoutScanningAllOfIt()
    {
        // The bound is what keeps an unbounded string out of a BCrypt loop that costs roughly a quarter of a
        // second per stored hash.
        MfaCodeFormat.IsRecoveryCodeShape(new string('A', 100_000)).Should().BeFalse();
    }
}