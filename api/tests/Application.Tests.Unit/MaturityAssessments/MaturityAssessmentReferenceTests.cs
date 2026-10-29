using Application.MaturityAssessments;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Unit.MaturityAssessments;

// "{financial year END year}-{sequence:0000}" — Figma's "2027-0197". Pinned against literals so the integration
// tests may derive their expectations from the formatter without the format itself being free to drift.
public class MaturityAssessmentReferenceTests
{
    [Theory]
    [InlineData("2026/27", 1, "2027-0001")]
    [InlineData("2026/27", 197, "2027-0197")]
    [InlineData("2026", 12, "2027-0012")]
    [InlineData("2099/00", 10_000, "2100-10000")]
    public void Format_BuildsTheEndYearAndPaddedSequence(string financialYear, int sequence, string expected)
    {
        MaturityAssessmentReference.Format(financialYear, sequence).Should().Be(expected);
    }

    [Theory]
    [InlineData("2026/27", 2027)]
    [InlineData("2025", 2026)]
    public void EndYear_IsTheYearTheFinancialYearEndsIn(string financialYear, int expected)
    {
        MaturityAssessmentReference.EndYear(financialYear).Should().Be(expected);
    }
}