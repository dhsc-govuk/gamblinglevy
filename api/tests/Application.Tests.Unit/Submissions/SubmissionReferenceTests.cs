using Application.Submissions;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Unit.Submissions;

// The reference number is the only identifier a user ever quotes back to DHSC, and its shape is a contract:
// "{financial year END year}{quarter:00}-{sequence:0000}". Until BLUM-7703's follow-up it was pinned only by
// literals like "202701-0001" scattered through the integration tests, which also hardcoded the financial year that
// produced them. Those tests now DERIVE the expected reference from the year under test, so this is what stops the
// format itself drifting — without it the derivation would only prove the formatter agrees with itself.
public class SubmissionReferenceTests
{
    [Theory]
    [InlineData("2026/27", 1, 1, "202701-0001")]
    [InlineData("2026/27", 4, 197, "202704-0197")]
    [InlineData("2026", 2, 12, "202702-0012")]
    [InlineData("2099/00", 3, 10_000, "210003-10000")]
    public void Format_BuildsTheEndYearQuarterAndPaddedSequence(string financialYear, int quarter, int sequence, string expected)
    {
        SubmissionReference.Format(financialYear, quarter, sequence).Should().Be(expected);
    }

    // The prefix is the year the financial year ENDS in, not the one it starts in — every quarter of 2026/27 shares
    // 2027, including Q1, which falls in calendar 2026.
    [Fact]
    public void Format_UsesTheYearTheFinancialYearEndsIn()
    {
        SubmissionReference.Format("2026/27", 1, 1).Should().StartWith("2027");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void Format_RejectsAQuarterOutsideTheFinancialYear(int quarter)
    {
        var act = () => SubmissionReference.Format("2026/27", quarter, 1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Format_RejectsASequenceBelowOne()
    {
        var act = () => SubmissionReference.Format("2026/27", 1, 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("banana")]
    [InlineData("26/27")]
    public void Format_RejectsAYearItCannotRead(string financialYear)
    {
        var act = () => SubmissionReference.Format(financialYear, 1, 1);

        act.Should().Throw<ArgumentException>();
    }
}