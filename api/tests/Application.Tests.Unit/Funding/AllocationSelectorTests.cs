using Application.Common.Funding;
using Application.Common.Interfaces.Repositories;
using Domain.Funding;
using Domain.Organisations;
using Domain.Users;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Unit.Funding;

// An organisation accumulates one allocation row per financial year (BLUM-7608), but the admin screens show
// exactly two. This is what decides which two.
public class AllocationSelectorTests
{
    private static readonly DateTime Now = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc); // FY 2026/27
    private static readonly OrganisationId Org = OrganisationId.New();

    [Fact]
    public void Select_ReturnsNothing_WhenTheOrganisationHasNoAllocations()
    {
        var (current, next) = AllocationSelector.Select([], Now);

        current.Should().BeNull();
        next.Should().BeNull();
    }

    [Fact]
    public void Select_PicksTheYearInProgressAndTheOneAfterIt()
    {
        var rows = new[] { Row("2025/26", 1m), Row("2026/27", 2m), Row("2027/28", 3m) };

        var (current, next) = AllocationSelector.Select(rows, Now);

        current!.Window.FinancialYear.Should().Be("2026/27");
        next!.Window.FinancialYear.Should().Be("2027/28");
    }

    // The regression the first cut of this had: FindLatestForOrganisationAsync ordered by CreatedAt, and create
    // writes the upcoming year's row SECOND, so "latest" meant next year. Selection must key off the financial
    // year, never insertion order — pass the rows newest-first to prove ordering is not being relied on.
    [Fact]
    public void Select_PicksTheYearInProgress_NotTheRowThatWasWrittenLast()
    {
        var rows = new[] { Row("2027/28", 3m), Row("2026/27", 2m) };

        var (current, next) = AllocationSelector.Select(rows, Now);

        current!.Allocation.AllocatedAmount.Should().Be(2m);
        current.Window.FinancialYear.Should().Be("2026/27");
        next!.Window.FinancialYear.Should().Be("2027/28");
    }

    // A report for a year nothing was allocated against must not be measured with money agreed later: an earlier
    // year is always preferred over a later one, and only a total absence of earlier years falls forward.
    [Fact]
    public void SelectForFinancialYear_PrefersAnEarlierYearOverALaterOne()
    {
        var rows = new[] { Row("2024/25", 1m), Row("2027/28", 3m) };

        var (current, _) = AllocationSelector.SelectForFinancialYear(rows, "2025/26");

        current!.Window.FinancialYear.Should().Be("2024/25");
    }

    [Fact]
    public void SelectForFinancialYear_FallsForwardOnlyWhenNothingEarlierExists()
    {
        var rows = new[] { Row("2026/27", 2m), Row("2027/28", 3m) };

        var (current, next) = AllocationSelector.SelectForFinancialYear(rows, "2025/26");

        current!.Window.FinancialYear.Should().Be("2026/27", "the earliest later year, not the latest one");
        next!.Window.FinancialYear.Should().Be("2027/28");
    }

    // An organisation onboarded in March holds only the coming year's figure. Showing it nothing would be wrong.
    [Fact]
    public void Select_FallsForwardToTheEarliestFutureYear_WhenThereIsNoRowForTheYearInProgress()
    {
        var rows = new[] { Row("2028/29", 2m), Row("2027/28", 1m) };

        var (current, next) = AllocationSelector.Select(rows, Now);

        current!.Window.FinancialYear.Should().Be("2027/28");
        next!.Window.FinancialYear.Should().Be("2028/29");
    }

    // The pre-BLUM-7608 shape: one row, windowed to whatever year the organisation was onboarded in. That is
    // still the only figure it has, and it is what the screens showed before this ticket.
    [Fact]
    public void Select_FallsBackToTheLatestPastYear_WhenEveryRowIsHistorical()
    {
        var rows = new[] { Row("2020/21", 1m), Row("2024/25", 2m) };

        var (current, next) = AllocationSelector.Select(rows, Now);

        current!.Window.FinancialYear.Should().Be("2024/25");
        next.Should().BeNull();
    }

    // A gap has to read as a gap: 2028/29 is not "next year" relative to 2026/27, and labelling it so would put
    // money on screen against a year it was never allocated to.
    [Fact]
    public void Select_ReturnsNoUpcomingYear_WhenTheFollowingYearIsMissing()
    {
        var rows = new[] { Row("2026/27", 1m), Row("2028/29", 2m) };

        var (current, next) = AllocationSelector.Select(rows, Now);

        current!.Window.FinancialYear.Should().Be("2026/27");
        next.Should().BeNull();
    }

    private static OrgAllocationWithWindow Row(string financialYear, decimal amount)
    {
        var window = FundingWindow.New(FundingWindowId.New(), financialYear, $"Annual Allocation {financialYear}", DateTime.UtcNow);
        var allocation = OrgFundingAllocation.New(OrgFundingAllocationId.New(), Org, window.Id, amount, UserId.New(), DateTime.UtcNow);
        return new OrgAllocationWithWindow(allocation, window);
    }
}
