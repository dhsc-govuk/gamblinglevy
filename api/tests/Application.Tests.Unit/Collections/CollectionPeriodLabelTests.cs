using Application.Collections;
using Domain.Collections;
using Domain.Users;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Unit.Collections;

// BLUM-7721 AC3 asks the LA email for "the quarter"; AC4 asks the DHSC email for "the quarter (or maturity
// assessment period)". One Notify slot, two vocabularies — this is what fills it.
public class CollectionPeriodLabelTests
{
    private static readonly DateOnly Open = new(2026, 9, 1);
    private static readonly DateOnly Deadline = new(2026, 9, 30);

    [Fact]
    public void For_RendersTheQuarterAndFinancialYear()
    {
        CollectionPeriodLabel.For(Build(CollectionType.QuarterlyReport, "2026/27", quarter: 2))
            .Should().Be("Quarter 2 2026/27");
    }

    [Fact]
    public void For_RendersMaturityAssessment_WhenThereIsNoQuarter()
    {
        // AC4's parenthetical. Collection.ValidateWindow forces Quarter to null for a non-quarterly type, so this
        // is the shape every maturity assessment actually has.
        CollectionPeriodLabel.For(Build(CollectionType.MaturityAssessment, "2026/27", quarter: null))
            .Should().Be("Maturity assessment 2026/27");
    }

    [Fact]
    public void For_FallsBackToTheStemAlone_WhenTheFinancialYearIsMissing()
    {
        // Only reachable on a draft, but the sweep is handed whatever is in the database.
        CollectionPeriodLabel.For(Draft(CollectionType.QuarterlyReport, financialYear: null, quarter: null))
            .Should().Be("Maturity assessment");
    }

    [Fact]
    public void For_DoesNotRepeatThePeriodDates()
    {
        // The emails carry reporting_period_start and reporting_period_end as their own personalisation fields,
        // so folding the dates into the label would print each of them twice in the template.
        var label = CollectionPeriodLabel.For(Build(CollectionType.QuarterlyReport, "2026/27", quarter: 2));

        label.Should().NotContain("2026-").And.NotContain("July").And.NotContain("September");
    }

    private static Collection Build(CollectionType type, string? financialYear, int? quarter)
        => Collection.New(
            CollectionId.New(),
            "Period label fixture",
            null,
            type,
            financialYear,
            quarter,
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 9, 30),
            false,
            Open,
            Deadline,
            true,
            UserId.New(),
            saveAsDraft: false,
            utcNow: DateTime.UtcNow);

    private static Collection Draft(CollectionType type, string? financialYear, int? quarter)
        => Collection.New(
            CollectionId.New(),
            "Period label draft fixture",
            null,
            type,
            financialYear,
            quarter,
            null,
            null,
            null,
            null,
            null,
            null,
            UserId.New(),
            saveAsDraft: true,
            utcNow: DateTime.UtcNow);
}