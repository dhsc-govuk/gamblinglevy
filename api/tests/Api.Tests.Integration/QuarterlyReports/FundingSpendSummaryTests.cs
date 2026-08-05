using Api.Dtos;
using Domain.Funding;
using Domain.Users;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Tests.Common;
using Xunit;

namespace Api.Tests.Integration.QuarterlyReports;

// The "Summary for all activities" figures (BLUM-6612): grant allocation, this-quarter / expected-for-year sums,
// and the "spend to date" / per-activity "total spent so far" accumulation across earlier quarters of the same
// financial year. Also proves the real ISpendReportTotals feeds the funding dashboard's cumulative spend.
public class FundingSpendSummaryTests(IntegrationTestWebFactory factory) : QuarterlyReportTestBase(factory)
{
    private static readonly string CurrentFy = ComputeCurrentFinancialYear();

    [Fact]
    public async Task Summary_ComputesGrantAllocationAndThisQuarterAndExpectedForYear()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await CreateOpenQuarterCollectionAsync(1, "Summary Q1", CurrentFy);
        var activity = await CreateRnaActivityAsync();
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);
        await SeedCurrentWindowAllocationAsync(50_000m);

        (await SaveFundingActualsAsync(collectionId, saveAsDraft: true, otherAmount: 500m, (activity.Id, 1_000m)))
            .EnsureSuccessStatusCode();

        var funding = await GetFundingSpendAsync(collectionId);
        var forecastActivities = new[]
        {
            new FundingActivityForecastInputDto(
                activity.Id,
                funding.RemainingQuarters.Select(q => new FundingQuarterAmountInputDto(q.Quarter, 200m)).ToList()),
        };
        var otherForecast = funding.RemainingQuarters
            .Select(q => new FundingQuarterAmountInputDto(q.Quarter, 100m))
            .ToList();
        (await SaveFundingForecastAsync(collectionId, saveAsDraft: true, forecastActivities, otherForecast)).EnsureSuccessStatusCode();

        var result = await GetFundingSpendAsync(collectionId);

        result.Summary.TotalGrantAllocation.Should().Be(50_000m);
        result.Summary.TotalSpendThisQuarter.Should().Be(1_500m);
        // Full year = £0 spend to date + £1,500 this quarter + 3 remaining quarters x (activity £200 + other £100).
        result.Summary.ExpectedSpendForYear.Should().Be(2_400m);
        result.Summary.SpendToDate.Should().Be(0m, "the first quarter has no earlier quarters to accumulate");
    }

    [Fact]
    public async Task SpendToDate_AccumulatesActualSpendFromEarlierQuartersOfTheSameYear()
    {
        AuthenticateAsMainOrgLead();

        // Quarter 1: record actual spend, then check Quarter 2 sees it as "spend to date".
        var q1 = await CreateOpenQuarterCollectionAsync(1, "Accumulation Q1", CurrentFy);
        var activity = await CreateRnaActivityAsync();
        await CreateSubmissionAsync(q1);
        await ConfirmActivitiesAsync(q1);
        (await SaveFundingActualsAsync(q1, saveAsDraft: true, otherAmount: 20m, (activity.Id, 100m)))
            .EnsureSuccessStatusCode();

        var q1Funding = await GetFundingSpendAsync(q1);
        q1Funding.Summary.SpendToDate.Should().Be(0m);
        q1Funding.Activities.Single().TotalSpentSoFar.Should().Be(0m);

        var q2 = await CreateOpenQuarterCollectionAsync(2, "Accumulation Q2", CurrentFy);
        await CreateSubmissionAsync(q2);
        await ConfirmActivitiesAsync(q2);

        var q2Funding = await GetFundingSpendAsync(q2);

        q2Funding.CurrentQuarter.Should().Be(2);
        q2Funding.RemainingQuarters.Select(q => q.Quarter).Should().Equal(3, 4);
        q2Funding.Summary.SpendToDate.Should().Be(120m, "Q1 actual was £100 on the activity plus £20 other");
        q2Funding.Activities.Single().TotalSpentSoFar.Should().Be(100m);
    }

    [Fact]
    public async Task FundingDashboard_CumulativeSpend_ReflectsCompletedFundingSpend()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await CreateOpenQuarterCollectionAsync(1, "Dashboard Q1", CurrentFy);
        await CreateRnaActivityAsync();
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);
        await SeedCurrentWindowAllocationAsync(50_000m);

        // One activity £100 + £20 other = £120 actual spent this quarter.
        await CompleteFundingSpendSectionAsync(collectionId, perActivityActual: 100m, otherActual: 20m);

        AuthenticateAsMainOrgLead();
        var response = await Client.GetAsync("funding/dashboard");
        response.EnsureSuccessStatusCode();
        var dashboard = await response.ToResponseModel<FundingDashboardDto>();

        dashboard.AllocatedAmount.Should().Be(50_000m);
        dashboard.CumulativeSpend.Should().Be(120m);
        dashboard.RemainingBudget.Should().Be(49_880m);
    }

    // Seeds an allocation for the current funding window (find-or-create the window so the resolver returns the same
    // one). The amount is the org's annual levy allocation surfaced as "Total grant allocation".
    private async Task SeedCurrentWindowAllocationAsync(decimal amount)
    {
        var window = await Context.FundingWindows.FirstOrDefaultAsync(w => w.FinancialYear == CurrentFy);
        if (window is null)
        {
            window = FundingWindow.New(FundingWindowId.New(), CurrentFy, $"Annual Allocation {CurrentFy}");
            await Context.FundingWindows.AddAsync(window);
            await SaveChangesAsync();
        }

        await Context.OrgFundingAllocations.AddAsync(OrgFundingAllocation.New(
            OrgFundingAllocationId.New(), MainOrg.Id, window.Id, amount, UserId.New()));
        await SaveChangesAsync();
    }

    // Mirrors FundingWindowResolver.CurrentFinancialYear (UK financial year boundary: 6 April).
    private static string ComputeCurrentFinancialYear()
    {
        var now = DateTime.UtcNow;
        var fyStartYear = (now.Month > 4 || (now.Month == 4 && now.Day >= 6)) ? now.Year : now.Year - 1;
        return $"{fyStartYear}/{(fyStartYear + 1) % 100:D2}";
    }
}