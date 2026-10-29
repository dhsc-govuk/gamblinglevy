using System.Globalization;
using Application.Common.ReportSnapshots;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Unit.ReportSnapshots;

/// <summary>
/// The grand total that closes the DHSC consolidated funding-spend.csv (BLUM-9021): pure row arithmetic over frozen,
/// already-rendered cells, so it is pinned here rather than through a container. DhscSubmissionExportTests covers the
/// flag and the export wiring.
/// </summary>
public class ReportSnapshotCsvTests
{
    private const string FinancialYear = "2026/27";

    private static readonly string[] Prefix = ["", "", "", ""];

    // Current-shape funding rows: Activity, Category, Activity ID, then the nine money columns of FundingHeader
    // (Q1 actual, Q2 forecast, Q2 actual, Q3 forecast, Q3 actual, Q4 forecast, Q4 actual, total to date, projected).
    private static ReportSnapshotCsvSource Source(string organisation, params string[][] rows) =>
        new(
            [organisation, "North East", "E06000001", "E1234"],
            new ReportSnapshotDocument([], new ReportSnapshotFunding(ReportSnapshotCsv.FundingHeader, rows, []), []));

    private static readonly ReportSnapshotCsvSource Alpha = Source(
        "Alpha Council",
        ["Alpha post", "Public health", "a1", "100.00", "40.00", "25.00", "", "", "", "", "125.00", "165.00"],
        ["Other (not linked to an activity)", "", "", "50.00", "", "(2.50)", "", "", "", "0.00", "47.50", "47.50"]);

    private static readonly ReportSnapshotCsvSource Beta = Source(
        "Beta Council",
        ["Beta post", "Housing", "b1", "150.00", "40.00", "", "", "", "", "", "150.00", "190.00"]);

    private static int Column(string name) => Prefix.Length + 1 + Array.IndexOf(ReportSnapshotCsv.FundingHeader, name);

    [Fact]
    public void FundingGrandTotalRow_TotalsEveryMoneyColumnAcrossEverySource()
    {
        var row = ReportSnapshotCsv.FundingGrandTotalRow([Alpha, Beta], Prefix, FinancialYear)!;

        row[Column("Q1 total actual (£)")].Should().Be("300.00");
        row[Column("Q2 Forecast (£)")].Should().Be("80.00");
        row[Column("Q2 total actual (£)")].Should().Be("22.50", "a negative correction is subtracted, not skipped");
        row[Column("Q3 Forecast (£)")].Should().BeEmpty("nobody recorded anything there, and blank is not 0.00");
        row[Column("Q4 total actual (£)")].Should().Be("0.00", "one row recorded zero, which is an answer");
        row[Column("Total actual to date (£)")].Should().Be("322.50");
        row[Column("Projected activity spend (£)")].Should().Be("402.50");
        row[Column("Category")].Should().BeEmpty();
        row[Column("Activity ID")].Should().BeEmpty();
    }

    [Fact]
    public void FundingGrandTotalRow_EqualsTheSumOfThePerOrganisationSubtotals()
    {
        // FundingRows' subtotals and the grand total read the same cells with the same parser; this is the invariant
        // a reader checks by eye, so it is pinned directly rather than trusted to follow from the shared code.
        var subtotals = ReportSnapshotCsv.FundingRows([Alpha, Beta], FinancialYear)
            .Where(row => row[^1] == ReportSnapshotCsv.SubtotalRowType)
            .ToList();
        var grandTotal = ReportSnapshotCsv.FundingGrandTotalRow([Alpha, Beta], Prefix, FinancialYear)!;

        subtotals.Should().HaveCount(2);
        foreach (var name in ReportSnapshotCsv.FundingHeader.Where(name => name.EndsWith("(£)", StringComparison.Ordinal)))
        {
            var column = Column(name);
            var recorded = subtotals.Select(row => row[column]).Where(cell => cell.Length > 0).ToList();
            if (recorded.Count == 0)
            {
                grandTotal[column].Should().BeEmpty();
                continue;
            }

            Parse(grandTotal[column]).Should().Be(recorded.Sum(Parse), "\"{0}\" is the sum of the subtotals", name);
        }
    }

    [Fact]
    public void FundingGrandTotalRow_IsTheCallersPrefix_TheFinancialYear_TheTotals_ThenItsRowType()
    {
        var row = ReportSnapshotCsv.FundingGrandTotalRow([Alpha], ["", "North East", "", ""], FinancialYear)!;

        row.Should().HaveCount(4 + 1 + ReportSnapshotCsv.FundingHeader.Length + 1, "it is exactly as wide as every other row");
        row.Take(4).Should().Equal("", "North East", "", "");
        row[4].Should().Be(FinancialYear);
        row[5].Should().Be(ReportSnapshotCsv.GrandTotalLabel);
        row[^1].Should().Be(ReportSnapshotCsv.GrandTotalRowType);
    }

    [Fact]
    public void FundingGrandTotalRow_WithoutAFinancialYear_HasNoCellForOne()
    {
        var row = ReportSnapshotCsv.FundingGrandTotalRow([Alpha], Prefix)!;

        row.Should().HaveCount(Prefix.Length + ReportSnapshotCsv.FundingHeader.Length + 1);
        row[Prefix.Length].Should().Be(ReportSnapshotCsv.GrandTotalLabel);
    }

    [Fact]
    public void FundingGrandTotalRow_IsNull_WhenNoSourceContributedAFundingRow()
    {
        // No row to total means no total, so an empty window's file stays header-only.
        var noFunding = new ReportSnapshotCsvSource(Prefix, new ReportSnapshotDocument([], null, []));
        var emptyFunding = Source("Empty Council");

        ReportSnapshotCsv.FundingGrandTotalRow([], Prefix, FinancialYear).Should().BeNull();
        ReportSnapshotCsv.FundingGrandTotalRow([noFunding, emptyFunding], Prefix, FinancialYear).Should().BeNull();
    }

    [Fact]
    public void FundingGrandTotalRow_IgnoresASourceWithNoFunding_AlongsideOnesThatHaveIt()
    {
        var noFunding = new ReportSnapshotCsvSource(Prefix, new ReportSnapshotDocument([], null, []));

        ReportSnapshotCsv.FundingGrandTotalRow([Alpha, noFunding, Beta], Prefix, FinancialYear)
            .Should().Equal(ReportSnapshotCsv.FundingGrandTotalRow([Alpha, Beta], Prefix, FinancialYear));
    }

    [Fact]
    public void FundingGrandTotalRow_IgnoresASourceFrozenInAnOlderTableShape()
    {
        // The row is wide enough that its £999 sits at the index of today's "Q1 total actual (£)" and "Q2 Forecast
        // (£)": totalled by the current header's indices, it would land there. The method filters it out itself
        // rather than trusting its caller to have done so.
        var legacy = new ReportSnapshotCsvSource(
            Prefix,
            new ReportSnapshotDocument(
                [],
                new ReportSnapshotFunding(
                    ["Activity", "Category", "Activity ID", "Levy spent this quarter (£)", "Total spent so far (£)"],
                    [["Legacy post", "Public health", "l1", "999.00", "999.00"]],
                    []),
                []));

        ReportSnapshotCsv.FundingGrandTotalRow([Alpha, legacy, Beta], Prefix, FinancialYear)
            .Should().Equal(ReportSnapshotCsv.FundingGrandTotalRow([Alpha, Beta], Prefix, FinancialYear));
        ReportSnapshotCsv.FundingGrandTotalRow([legacy], Prefix, FinancialYear)
            .Should().BeNull("an older-shape source contributes no row to total");
    }

    [Fact]
    public void FundingGrandTotalRow_RendersANegativeTotalInAccountingForm()
    {
        // A leading '-' would be neutralised by CsvWriter's formula-injection guard and turn the total into text.
        var refund = Source(
            "Refund Council",
            ["Other (not linked to an activity)", "", "", "(400.00)", "", "", "", "", "", "", "(400.00)", "(400.00)"]);

        var row = ReportSnapshotCsv.FundingGrandTotalRow([Alpha, refund], Prefix, FinancialYear)!;

        row[Column("Q1 total actual (£)")].Should().Be("(250.00)");
        row.Should().NotContain(cell => cell.StartsWith('-'));
    }

    [Fact]
    public void FundingRows_MarksAnActivityNamedGrandTotalAsAnActivity()
    {
        // Activity names are free text (BLUM-7620), so an LA can type the label. Row type is the marker, not the label.
        var named = Source(
            "Named Council",
            [ReportSnapshotCsv.GrandTotalLabel, "Housing", "n1", "10.00", "", "", "", "", "", "", "10.00", "10.00"]);

        var rows = ReportSnapshotCsv.FundingRows([named], FinancialYear);

        rows[0][^1].Should().Be(ReportSnapshotCsv.ActivityRowType);
        rows.Should().NotContain(row => row[row.Count - 1] == ReportSnapshotCsv.GrandTotalRowType);
    }

    private static decimal Parse(string cell) => cell.StartsWith('(')
        ? -decimal.Parse(cell[1..^1], CultureInfo.InvariantCulture)
        : decimal.Parse(cell, CultureInfo.InvariantCulture);
}