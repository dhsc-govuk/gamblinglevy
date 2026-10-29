using Application.Common.Funding;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Unit.Funding;

// The "YYYY/YY" financial-year string is matched by exact equality everywhere it is used
// (IFundingWindowRepository.FindByFinancialYearAsync, ISubmissionQueries.GetForOrgAndFinancialYearAsync), so the
// format is a contract rather than a display choice. This logic lived untested inside FundingWindowResolver until
// BLUM-7608 made the year something an admin chooses rather than something the clock stamps.
public class FinancialYearRangeTests
{
    // The UK government financial year runs 1 April - 31 March. 31 March still belongs to the year that started
    // the previous April; 1 April is the first day of the new one. Deliberately NOT the 6 April personal tax-year
    // boundary this logic used before BLUM-7608 — 1-5 April is exactly where the two disagreed, and where an
    // organisation was allocated against a year its own quarterly reports had already moved past.
    [Theory]
    [InlineData(2026, 3, 31, "2025/26")]
    [InlineData(2026, 4, 1, "2026/27")]
    [InlineData(2026, 4, 5, "2026/27")]
    [InlineData(2026, 4, 6, "2026/27")]
    [InlineData(2026, 1, 1, "2025/26")]
    [InlineData(2026, 12, 31, "2026/27")]
    public void Current_PutsTheDateInTheRightFinancialYear(int year, int month, int day, string expected)
    {
        FinancialYearRange.Current(new DateTime(year, month, day, 12, 0, 0, DateTimeKind.Utc)).Should().Be(expected);
    }

    // The end year is two digits, so it wraps at the century. Without the modulo it would render "1999/100".
    [Fact]
    public void Current_WrapsTheEndYearAtTheCentury()
    {
        FinancialYearRange.Current(new DateTime(1999, 6, 1, 0, 0, 0, DateTimeKind.Utc)).Should().Be("1999/00");
    }

    [Theory]
    [InlineData("2026/27", "2027/28")]
    [InlineData("1999/00", "2000/01")]
    [InlineData("2098/99", "2099/00")]
    public void Next_ReturnsTheFollowingFinancialYear(string financialYear, string expected)
    {
        FinancialYearRange.Next(financialYear).Should().Be(expected);
    }

    [Fact]
    public void Next_ThrowsOnAMalformedYear()
    {
        var act = () => FinancialYearRange.Next("2026");
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("2027/28", "2026/27")]
    [InlineData("2000/01", "1999/00")]
    [InlineData("2099/00", "2098/99")]
    public void Previous_ReturnsThePrecedingFinancialYear(string financialYear, string expected)
    {
        FinancialYearRange.Previous(financialYear).Should().Be(expected);
    }

    [Fact]
    public void Previous_ThrowsOnAMalformedYear()
    {
        var act = () => FinancialYearRange.Previous("2026");
        act.Should().Throw<ArgumentException>();
    }

    // Round-trips: the pair has to agree, or a test shifting a year one way and asserting the other drifts silently.
    [Theory]
    [InlineData("2026/27")]
    [InlineData("1999/00")]
    public void PreviousAndNext_AreInverses(string financialYear)
    {
        FinancialYearRange.Next(FinancialYearRange.Previous(financialYear)).Should().Be(financialYear);
        FinancialYearRange.Previous(FinancialYearRange.Next(financialYear)).Should().Be(financialYear);
    }

    [Theory]
    [InlineData("2026/27")]
    [InlineData("1999/00")]
    public void IsValid_AcceptsAWellFormedYear(string financialYear)
    {
        FinancialYearRange.IsValid(financialYear).Should().BeTrue();
    }

    // "2026/28" is the case worth naming: it parses, but it names a two-year span no funding window can mean, and
    // accepting it would create a window nothing else in the system ever matches.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2026")]
    [InlineData("2026/2027")]
    [InlineData("2026-27")]
    [InlineData("2026/28")]
    [InlineData("2026/26")]
    [InlineData("banana")]
    [InlineData(" 2026/27")]
    public void IsValid_RejectsAnythingElse(string? financialYear)
    {
        FinancialYearRange.IsValid(financialYear).Should().BeFalse();
    }

    // Round-trip guard: whatever Current produces must be something IsValid accepts, or a clock-stamped window
    // would be rejected by the validator that guards the admin-chosen one.
    [Fact]
    public void Current_AlwaysProducesAValidFinancialYear()
    {
        for (var offsetDays = 0; offsetDays < 400; offsetDays++)
        {
            var date = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(offsetDays);
            FinancialYearRange.IsValid(FinancialYearRange.Current(date)).Should().BeTrue();
        }
    }
}
