using Application.Collections;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Unit.Collections;

public class ReportingPeriodTests
{
    [Theory]
    [InlineData(1, 2026, 4, 1, 2026, 6, 30)]
    [InlineData(2, 2026, 7, 1, 2026, 9, 30)]
    [InlineData(3, 2026, 10, 1, 2026, 12, 31)]
    [InlineData(4, 2027, 1, 1, 2027, 3, 31)]
    public void Derive_QuarterlyReport_ReturnsUkFiscalQuarterBoundaries(
        int quarter,
        int startYear,
        int startMonth,
        int startDay,
        int endYear,
        int endMonth,
        int endDay)
    {
        var (start, end) = ReportingPeriod.Derive("2026", quarter);

        start.Should().Be(new DateOnly(startYear, startMonth, startDay));
        end.Should().Be(new DateOnly(endYear, endMonth, endDay));
    }

    [Fact]
    public void Derive_NoQuarter_ReturnsFullFinancialYear()
    {
        var (start, end) = ReportingPeriod.Derive("2026", null);

        start.Should().Be(new DateOnly(2026, 4, 1));
        end.Should().Be(new DateOnly(2027, 3, 31));
    }

    [Theory]
    [InlineData("2026")]
    [InlineData("2026/27")]
    [InlineData("2026-2027")]
    [InlineData(" 2026 ")]
    public void Derive_ParsesLeadingYearFromVariousFinancialYearFormats(string financialYear)
    {
        var (start, end) = ReportingPeriod.Derive(financialYear, 1);

        start.Should().Be(new DateOnly(2026, 4, 1));
        end.Should().Be(new DateOnly(2026, 6, 30));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("FY26")]
    [InlineData("26")]
    public void Derive_InvalidFinancialYear_Throws(string financialYear)
    {
        var act = () => ReportingPeriod.Derive(financialYear, 1);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void Derive_QuarterOutOfRange_Throws(int quarter)
    {
        var act = () => ReportingPeriod.Derive("2026", quarter);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(1, new[] { 2, 3, 4 })]
    [InlineData(2, new[] { 3, 4 })]
    [InlineData(3, new[] { 4 })]
    [InlineData(4, new int[] { })]
    public void RemainingQuarters_ReturnsQuartersAfterCurrent(int currentQuarter, int[] expected)
    {
        ReportingPeriod.RemainingQuarters(currentQuarter).Should().Equal(expected);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void RemainingQuarters_OutOfRange_Throws(int currentQuarter)
    {
        var act = () => ReportingPeriod.RemainingQuarters(currentQuarter);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}