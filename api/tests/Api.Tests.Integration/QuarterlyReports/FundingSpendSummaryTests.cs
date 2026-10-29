using Api.Dtos;
using Domain.Activities;
using Domain.Collections;
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
    private static readonly string CurrentFy = TestFinancialYears.Current;
    private static readonly string PreviousFy = TestFinancialYears.Previous(CurrentFy);

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

        // Export-only totals (BLUM-7130 CSV review). TotalSpendToDate includes the current quarter's own £1,500 —
        // there is nothing "prior" in Q1, so it equals TotalSpendThisQuarter exactly.
        result.Summary.TotalSpendToDate.Should().Be(1_500m);
        result.Summary.RemainingBudget.Should().Be(48_500m, "£50,000 allocation minus the £1,500 spent to date");
        result.Summary.TotalForecast.Should().Be(900m, "3 remaining quarters x (activity £200 + other £100)");
        result.Summary.ProjectedVariance.Should().Be(47_600m, "£50,000 allocation minus the £2,400 projected year-end spend");
    }

    [Fact]
    public async Task TotalActualToDateAndTotalSpendToDate_IncludeTheCurrentQuarterUnlikeTheLegacyFields()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await CreateOpenQuarterCollectionAsync(1, "Includes current quarter Q1", CurrentFy);
        var activity = await CreateRnaActivityAsync();
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);
        await SeedCurrentWindowAllocationAsync(1_000m);

        (await SaveFundingActualsAsync(collectionId, saveAsDraft: true, otherAmount: 20m, (activity.Id, 100m)))
            .EnsureSuccessStatusCode();

        var funding = await GetFundingSpendAsync(collectionId);
        var activityDto = funding.Activities.Single();

        // The pre-existing fields deliberately exclude the current quarter — nothing "prior" exists yet in Q1, so
        // both read zero even though £120 was just recorded.
        funding.Summary.SpendToDate.Should().Be(0m);
        activityDto.TotalSpentSoFar.Should().Be(0m);

        // The new export-only totals include this quarter's own £120 (£100 activity + £20 other).
        funding.Summary.TotalSpendToDate.Should().Be(120m);
        activityDto.TotalActualToDate.Should().Be(100m);
        funding.Summary.RemainingBudget.Should().Be(880m, "£1,000 allocation minus the £120 spent to date");

        // ActualByQuarter/ForecastByQuarter cover every quarter of the year, not just what's "remaining" from here.
        activityDto.ActualByQuarter[1].Should().Be(100m);
        activityDto.ActualByQuarter[2].Should().BeNull("Q2 hasn't happened yet, so it is blank rather than zero");
        activityDto.ForecastByQuarter.Should().ContainKeys(2, 3, 4);
    }

    [Fact]
    public async Task TotalSpendToDate_ReflectsOnlyTheMostRecentSave_NotEveryPriorSaveOfTheSameSubmission()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await CreateOpenQuarterCollectionAsync(1, "Resave replaces Q1", CurrentFy);
        var activity = await CreateRnaActivityAsync();
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);
        await SeedCurrentWindowAllocationAsync(1_000m);

        // First save, as a draft — the LA entering a first-pass figure.
        (await SaveFundingActualsAsync(collectionId, saveAsDraft: true, otherAmount: 20m, (activity.Id, 100m)))
            .EnsureSuccessStatusCode();

        // Re-save with different amounts — the LA correcting the figure before submitting, exactly what testing
        // a form by entering a value, saving, then changing it looks like.
        (await SaveFundingActualsAsync(collectionId, saveAsDraft: true, otherAmount: 10m, (activity.Id, 50m)))
            .EnsureSuccessStatusCode();

        var funding = await GetFundingSpendAsync(collectionId);

        // ReplaceForSubmissionAsync must have removed the first save's rows before adding the second's. If it
        // instead accumulated both saves, this reads £180 (100+20+50+10) instead of £60 (50+10).
        funding.Summary.TotalSpendToDate.Should().Be(60m);
        funding.Activities.Single().ActualThisQuarter.Should().Be(50m);

        var rawEntryCount = await Context.SubmissionSpendEntries.AsNoTracking().CountAsync();
        rawEntryCount.Should().Be(2, "one activity row + one other row — the first save's rows must be gone, not just superseded");
    }

    [Fact]
    public async Task TotalSpendToDate_ReflectsOnlyOneSave_WhenTwoSavesOfTheSameSubmissionRaceEachOther()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await CreateOpenQuarterCollectionAsync(1, "Concurrent resave Q1", CurrentFy);
        var activity = await CreateRnaActivityAsync();
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);
        await SeedCurrentWindowAllocationAsync(1_000m);

        // Saves of the SAME submission fired concurrently — a double-click on Save, or a client retry after a slow
        // response it gave up waiting on. ReplaceForSubmissionAsync reads "existing rows for this submission",
        // decides what to remove, then removes and adds. If two requests read "existing" before either commits,
        // neither sees the other's rows to remove and both sets survive — which is what the submission row lock
        // it takes first prevents.
        //
        // Eight, not two. The lock makes the count irrelevant to passing; it is there for the day the lock is
        // lost. Two requests rarely overlap closely enough to duplicate, so with two this test passed against the
        // unlocked code on most runs and failed CI at random. Eight contenders collide reliably.
        const int concurrentSaves = 8;
        var saves = Enumerable.Range(0, concurrentSaves)
            .Select(_ => SaveFundingActualsAsync(collectionId, saveAsDraft: true, otherAmount: 20m, (activity.Id, 100m)))
            .ToList();
        var responses = await Task.WhenAll(saves);

        foreach (var response in responses)
        {
            response.EnsureSuccessStatusCode();
        }

        var funding = await GetFundingSpendAsync(collectionId);

        // Whichever save "won" should be the only one left. Any multiple of £120 means the race duplicated rows.
        funding.Summary.TotalSpendToDate.Should().Be(120m);

        var rawEntryCount = await Context.SubmissionSpendEntries.AsNoTracking().CountAsync();
        rawEntryCount.Should().Be(2, "one activity row + one other row — every duplicated save leaves two more");
    }

    [Fact]
    public async Task TotalSpendToDate_ExcludesADraftAlreadySavedAgainstALaterQuarter()
    {
        AuthenticateAsMainOrgLead();

        // Q1: £3,850 actual, saved as a draft (never submitted).
        var q1 = await CreateOpenQuarterCollectionAsync(1, "No leak from later quarter Q1", CurrentFy);
        var activity = await CreateRnaActivityAsync();
        await CreateSubmissionAsync(q1);
        await ConfirmActivitiesAsync(q1);
        (await SaveFundingActualsAsync(q1, saveAsDraft: true, otherAmount: 850m, (activity.Id, 3_000m)))
            .EnsureSuccessStatusCode();

        var q1FundingBeforeQ2Exists = await GetFundingSpendAsync(q1);
        q1FundingBeforeQ2Exists.Summary.TotalSpendToDate.Should().Be(3_850m);

        // Q2 collection opens and £1,350 actual is saved there too — also as a draft, also never submitted.
        var q2 = await CreateOpenQuarterCollectionAsync(2, "No leak from later quarter Q2", CurrentFy);
        await CreateSubmissionAsync(q2);
        await ConfirmActivitiesAsync(q2);
        (await SaveFundingActualsAsync(q2, saveAsDraft: true, otherAmount: 350m, (activity.Id, 1_000m)))
            .EnsureSuccessStatusCode();

        // Q1's own TotalSpendToDate must be unaffected by Q2's later, unsubmitted draft.
        var q1FundingAfterQ2Drafted = await GetFundingSpendAsync(q1);
        q1FundingAfterQ2Drafted.Summary.TotalSpendToDate.Should().Be(
            3_850m,
            "Q2's draft actual spend is a later quarter's data Q1's own report has no business including");
    }

    [Fact]
    public async Task TotalSpendToDate_ExcludesASoftDeletedDuplicateCollectionSharingTheSameQuarter()
    {
        AuthenticateAsMainOrgLead();

        // A mistaken/duplicate Q1 collection — the shape a recreated test collection leaves behind — with its own
        // submission and £3,850 actual, later soft-deleted.
        var deletedQ1 = await CreateOpenQuarterCollectionAsync(1, "Duplicate Q1 (later deleted)", CurrentFy);
        var activity = await CreateRnaActivityAsync();
        await CreateSubmissionAsync(deletedQ1);
        await ConfirmActivitiesAsync(deletedQ1);
        (await SaveFundingActualsAsync(deletedQ1, saveAsDraft: true, otherAmount: 850m, (activity.Id, 3_000m)))
            .EnsureSuccessStatusCode();

        await Context.Collections
            .Where(c => c.Id == new CollectionId(deletedQ1))
            .ExecuteUpdateAsync(c => c.SetProperty(x => x.DeletedAt, DateTime.UtcNow));
        Context.ChangeTracker.Clear();

        // The org's real Q1 collection, recreated after the mistaken one was deleted, with its own £200 actual.
        var q1 = await CreateOpenQuarterCollectionAsync(1, "The real Q1", CurrentFy);
        await CreateSubmissionAsync(q1);
        await ConfirmActivitiesAsync(q1);
        (await SaveFundingActualsAsync(q1, saveAsDraft: true, otherAmount: 100m, (activity.Id, 100m)))
            .EnsureSuccessStatusCode();

        var funding = await GetFundingSpendAsync(q1);

        funding.Summary.TotalSpendToDate.Should().Be(
            200m,
            "the deleted duplicate Q1 collection's £3,850 must not double-count on top of the real Q1 submission's own £200");
    }

    [Fact]
    public async Task Forecast_MostRecentSubmissionWinsOverAnEarlierEstimateForTheSameFutureQuarter()
    {
        AuthenticateAsMainOrgLead();

        // Q1 forecasts £100 for every remaining quarter, including Q3.
        var q1 = await CreateOpenQuarterCollectionAsync(1, "Forecast revision Q1", CurrentFy);
        var activity = await CreateRnaActivityAsync();
        await CreateSubmissionAsync(q1);
        await ConfirmActivitiesAsync(q1);

        var q1Funding = await GetFundingSpendAsync(q1);
        var q1Forecast = new[]
        {
            new FundingActivityForecastInputDto(
                activity.Id,
                q1Funding.RemainingQuarters.Select(q => new FundingQuarterAmountInputDto(q.Quarter, 100m)).ToList())
        };
        var q1Other = q1Funding.RemainingQuarters.Select(q => new FundingQuarterAmountInputDto(q.Quarter, 0m)).ToList();
        (await SaveFundingForecastAsync(q1, saveAsDraft: true, q1Forecast, q1Other)).EnsureSuccessStatusCode();

        // Q2 revises the estimate for Q3 to £150 (its own more recent guess), leaving Q4 unchanged at £100.
        var q2 = await CreateOpenQuarterCollectionAsync(2, "Forecast revision Q2", CurrentFy);
        await CreateSubmissionAsync(q2);
        await ConfirmActivitiesAsync(q2);

        var q2Funding = await GetFundingSpendAsync(q2);
        q2Funding.RemainingQuarters.Select(q => q.Quarter).Should().Equal(3, 4);

        var q2Forecast = new[]
        {
            new FundingActivityForecastInputDto(
                activity.Id,
                [new FundingQuarterAmountInputDto(3, 150m), new FundingQuarterAmountInputDto(4, 100m)])
        };
        var q2Other = new List<FundingQuarterAmountInputDto> { new(3, 0m), new(4, 0m) };
        (await SaveFundingForecastAsync(q2, saveAsDraft: true, q2Forecast, q2Other)).EnsureSuccessStatusCode();

        // Q3's own report never forecasts its own current quarter — its view of "Q3 Forecast" comes entirely from
        // the historical resolution across Q1 and Q2, and the more recent (Q2) estimate must win.
        var q3 = await CreateOpenQuarterCollectionAsync(3, "Forecast revision Q3", CurrentFy);
        await CreateSubmissionAsync(q3);
        await ConfirmActivitiesAsync(q3);

        var q3Activity = (await GetFundingSpendAsync(q3)).Activities.Single();

        q3Activity.ForecastByQuarter[3].Should().Be(150m, "Q2's more recent estimate for Q3 must win over Q1's earlier £100 guess");
        q3Activity.ForecastByQuarter[4].Should().Be(100m, "unchanged between Q1 and Q2, so either submission's value is correct");
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
    public async Task ProjectedActivitySpend_ExcludesQuartersAlreadyActualised()
    {
        AuthenticateAsMainOrgLead();

        // Q1: actual £90, forecasts £100 for each of the (then) remaining quarters 2, 3 and 4.
        var q1 = await CreateOpenQuarterCollectionAsync(1, "Projected spend Q1", CurrentFy);
        var activity = await CreateRnaActivityAsync();
        await CreateSubmissionAsync(q1);
        await ConfirmActivitiesAsync(q1);
        (await SaveFundingActualsAsync(q1, saveAsDraft: true, otherAmount: 0m, (activity.Id, 90m))).EnsureSuccessStatusCode();

        var q1Funding = await GetFundingSpendAsync(q1);
        var q1Forecast = new[]
        {
            new FundingActivityForecastInputDto(
                activity.Id,
                q1Funding.RemainingQuarters.Select(q => new FundingQuarterAmountInputDto(q.Quarter, 100m)).ToList())
        };
        var q1Other = q1Funding.RemainingQuarters.Select(q => new FundingQuarterAmountInputDto(q.Quarter, 0m)).ToList();
        (await SaveFundingForecastAsync(q1, saveAsDraft: true, q1Forecast, q1Other)).EnsureSuccessStatusCode();

        // Q2: actual £110 — Q1's £100 forecast for Q2 is now stale.
        var q2 = await CreateOpenQuarterCollectionAsync(2, "Projected spend Q2", CurrentFy);
        await CreateSubmissionAsync(q2);
        await ConfirmActivitiesAsync(q2);
        (await SaveFundingActualsAsync(q2, saveAsDraft: true, otherAmount: 0m, (activity.Id, 110m))).EnsureSuccessStatusCode();

        // Q3: actual £120 — Q1's £100 forecast for Q3 is now stale too. Only Q4's £100 forecast is still genuinely
        // ahead of this report, so the projection must be £320 actual-to-date + £100, not + all three forecasts.
        var q3 = await CreateOpenQuarterCollectionAsync(3, "Projected spend Q3", CurrentFy);
        await CreateSubmissionAsync(q3);
        await ConfirmActivitiesAsync(q3);
        (await SaveFundingActualsAsync(q3, saveAsDraft: true, otherAmount: 0m, (activity.Id, 120m))).EnsureSuccessStatusCode();

        var q3Activity = (await GetFundingSpendAsync(q3)).Activities.Single();

        q3Activity.TotalActualToDate.Should().Be(320m, "£90 + £110 + £120 across Q1-Q3");
        q3Activity.ProjectedActivitySpend.Should().Be(
            420m, "£320 actual to date plus only Q4's still-future £100 forecast, not Q2/Q3's now-stale forecasts too");
    }

    [Fact]
    public async Task TotalGrantAllocation_IsFoundEvenWhenSeededUnderADifferentFinancialYearWindow()
    {
        // The allocation is windowed to an onboarding-era financial year that has nothing to do with the collection
        // being reported on — the shape every organisation created before BLUM-7608 still has, and the one the bulk
        // import still produces: exactly one OrgFundingAllocation row, windowed to whatever FY was current when it
        // was created, that amendments update in place rather than ever re-windowing.
        //
        // This is why AllocationSelector falls back to the latest EARLIER year rather than requiring an exact
        // match. A bare year-scoped lookup would match only for a collection whose year happens to equal the
        // org's onboarding year, and would silently report £0 for every other one.
        AuthenticateAsMainOrgLead();
        await SeedAllocationForFinancialYearAsync("2020/21", 50_000m);

        var collectionId = await CreateOpenQuarterCollectionAsync(1, "Allocation lookup Q1", CurrentFy);
        await CreateRnaActivityAsync();
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);

        var funding = await GetFundingSpendAsync(collectionId);

        funding.Summary.TotalGrantAllocation.Should().Be(50_000m);
    }

    // BLUM-7608: allocations are now versioned per financial year, so a report is measured against the
    // allocation for ITS OWN year. Without this, recording next year's figure would silently re-baseline every
    // remaining report of the year in progress.
    [Fact]
    public async Task TotalGrantAllocation_UsesTheAllocationForTheCollectionsOwnFinancialYear()
    {
        AuthenticateAsMainOrgLead();
        var nextFy = TestFinancialYears.Next(CurrentFy);
        await SeedAllocationForFinancialYearAsync(CurrentFy, 50_000m);
        await SeedAllocationForFinancialYearAsync(nextFy, 80_000m);

        var collectionId = await CreateOpenQuarterCollectionAsync(1, "FY scoped Q1", CurrentFy);
        await CreateRnaActivityAsync();
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);

        var funding = await GetFundingSpendAsync(collectionId);

        funding.Summary.TotalGrantAllocation.Should().Be(50_000m, "the collection is for the current financial year");
        funding.Summary.GrantAllocationFinancialYear.Should().Be(CurrentFy);

        // Export-only: the upcoming year's figure rides along for funding-summary.csv without touching any
        // number derived from this report's own allocation.
        funding.Summary.NextYearGrantAllocation.Should().Be(80_000m);
        funding.Summary.NextYearFinancialYear.Should().Be(nextFy);
        funding.Summary.RemainingBudget.Should().Be(50_000m, "the upcoming year's allocation must not enter this year's arithmetic");
    }

    [Fact]
    public async Task TotalGrantAllocation_PicksTheLaterYear_ForACollectionInThatYear()
    {
        AuthenticateAsMainOrgLead();
        var nextFy = TestFinancialYears.Next(CurrentFy);
        await SeedAllocationForFinancialYearAsync(CurrentFy, 50_000m);
        await SeedAllocationForFinancialYearAsync(nextFy, 80_000m);

        var collectionId = await CreateOpenQuarterCollectionAsync(1, "FY scoped next year Q1", nextFy);
        await CreateRnaActivityAsync();
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);

        var funding = await GetFundingSpendAsync(collectionId);

        funding.Summary.TotalGrantAllocation.Should().Be(80_000m);
        funding.Summary.GrantAllocationFinancialYear.Should().Be(nextFy);
        funding.Summary.NextYearGrantAllocation.Should().BeNull("nothing is recorded for the year after that one");
    }

    // Regression: the fallback used to be "the organisation's latest allocation by CreatedAt", which for an org
    // holding two years is the LATER one. A late report for an earlier year would then be measured against money
    // that had not been agreed when it was authored, and the frozen CSV would label it with that later year.
    [Fact]
    public async Task TotalGrantAllocation_PrefersAnEarlierYear_ForACollectionOlderThanEveryAllocation()
    {
        AuthenticateAsMainOrgLead();
        var nextFy = TestFinancialYears.Next(CurrentFy);
        await SeedAllocationForFinancialYearAsync(CurrentFy, 50_000m);
        await SeedAllocationForFinancialYearAsync(nextFy, 80_000m);

        // Older than both allocations: nothing at or before it exists, so it falls forward to the EARLIEST
        // later year rather than the latest.
        var collectionId = await CreateOpenQuarterCollectionAsync(1, "Old year Q1", "2019/20");
        await CreateRnaActivityAsync();
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);

        var funding = await GetFundingSpendAsync(collectionId);

        funding.Summary.TotalGrantAllocation.Should().Be(50_000m);
        funding.Summary.GrantAllocationFinancialYear.Should().Be(CurrentFy);
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

    // --- BLUM-7701: the dashboard tile follows the LA's own reporting year ------------------------------------

    // The organisation shape production is full of: a single allocation row created at onboarding and never
    // re-windowed, so an LA onboarded in 2025/26 still holds only that row while reporting in 2026/27. Binding the
    // spend total to the ALLOCATION's window meant summing a year the org has no submissions in, and "Spent to
    // date" stayed at £0 however many reports it submitted. The report screen already resolved the allocation for
    // the COLLECTION's year, so the two pages disagreed about the same money.
    [Fact]
    public async Task FundingDashboard_CumulativeSpend_FollowsTheReportingYear_WhenTheAllocationIsWindowedToAnEarlierOne()
    {
        var earlierFy = PreviousFy;

        AuthenticateAsMainOrgLead();
        var collectionId = await CreateOpenQuarterCollectionAsync(2, "Stale-allocation Q2", CurrentFy);
        await CreateRnaActivityAsync();
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);
        await SeedAllocationForFinancialYearAsync(earlierFy, 50_000m);

        await CompleteFundingSpendSectionAsync(collectionId, perActivityActual: 100m, otherActual: 20m);

        AuthenticateAsMainOrgLead();
        var dashboard = await GetDashboardAsync();
        var report = await GetFundingSpendAsync(collectionId);

        dashboard.FinancialYear.Should().Be(CurrentFy, "the tile describes the year in progress, not the year the allocation row is windowed to");
        dashboard.CumulativeSpend.Should().Be(120m);
        dashboard.AllocatedAmount.Should().Be(50_000m, "an allocation is never re-windowed, so the earlier row is still this year's money");
        dashboard.RemainingBudget.Should().Be(49_880m);

        // The two surfaces must agree about the same money.
        report.Summary.TotalGrantAllocation.Should().Be(dashboard.AllocatedAmount);
        report.Summary.TotalSpendToDate.Should().Be(dashboard.CumulativeSpend);
    }

    // Submitting is not what moves the tile — the Actual rows are written when the LA SAVES the funding screens,
    // draft included (see ISpendReportTotals). Submitting must leave the figure exactly where the save put it.
    [Fact]
    public async Task FundingDashboard_CumulativeSpend_MovesOnSave_AndIsUnchangedBySubmitting()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await CreateOpenQuarterCollectionAsync(2, "Save then submit Q2", CurrentFy);
        var activity = await CreateRnaActivityAsync();
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);
        await SeedCurrentWindowAllocationAsync(50_000m);

        AuthenticateAsMainOrgLead();
        (await GetDashboardAsync()).CumulativeSpend.Should().Be(0m);

        await CompleteFundingSpendSectionAsync(collectionId, perActivityActual: 100m, otherActual: 20m);

        AuthenticateAsMainOrgLead();
        (await GetDashboardAsync()).CumulativeSpend.Should().Be(120m, "the spend rows are written when the screen is saved");

        await CompleteActivityReportAsync(collectionId, activity.Id);
        await CompleteStrategicFeedbackSectionAsync(collectionId);
        (await SubmitReportAsync(collectionId)).EnsureSuccessStatusCode();

        AuthenticateAsMainOrgLead();
        (await GetDashboardAsync()).CumulativeSpend.Should().Be(120m, "submitting freezes the report, it does not re-record the spend");
    }

    // The other half of the year bound: a report for a DIFFERENT year contributes nothing to this year's tile.
    // Allocation, spend and remaining budget all have to belong to one year or "remaining budget" belongs to none.
    [Fact]
    public async Task FundingDashboard_CumulativeSpend_ExcludesAReportForAnotherFinancialYear()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await CreateOpenQuarterCollectionAsync(2, "Earlier-year Q2", PreviousFy);
        await CreateRnaActivityAsync();
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);
        await SeedCurrentWindowAllocationAsync(50_000m);

        await CompleteFundingSpendSectionAsync(collectionId, perActivityActual: 100m, otherActual: 20m);

        AuthenticateAsMainOrgLead();
        var dashboard = await GetDashboardAsync();

        dashboard.FinancialYear.Should().Be(CurrentFy);
        dashboard.CumulativeSpend.Should().Be(0m);
        dashboard.RemainingBudget.Should().Be(50_000m);
    }

    // --- BLUM-7607: every recorded amount survives into the quarterly breakdown -------------------------------

    [Fact]
    public async Task QuarterlyActuals_SumToTotalSpendToDate_WhenAnActivityWasDeletedAfterAnEarlierQuarter()
    {
        AuthenticateAsMainOrgLead();

        // Q1: £100 against each of two activities, plus £20 "Other".
        var q1 = await CreateOpenQuarterCollectionAsync(1, "Orphaned spend Q1", CurrentFy);
        var kept = await CreateRnaActivityAsync(title: "Kept activity");
        var removed = await CreateRnaActivityAsync(title: "Removed activity");
        await CreateSubmissionAsync(q1);
        await ConfirmActivitiesAsync(q1);
        (await SaveFundingActualsAsync(q1, saveAsDraft: true, otherAmount: 20m, (kept.Id, 100m), (removed.Id, 100m)))
            .EnsureSuccessStatusCode();

        // The LA then deletes one of them. Its Q1 spend was real, recorded and reported — it must not vanish.
        (await Client.DeleteAsync($"activities/{removed.Id}")).EnsureSuccessStatusCode();

        var q2 = await CreateOpenQuarterCollectionAsync(2, "Orphaned spend Q2", CurrentFy);
        await CreateSubmissionAsync(q2);
        await ConfirmActivitiesAsync(q2);

        var funding = await GetFundingSpendAsync(q2);

        funding.TotalActualByQuarter[1].Should().Be(220m, "£100 + £100 + £20 were all recorded in Q1");
        funding.Summary.TotalSpendToDate.Should().Be(220m);
        SumOfQuarterlyActuals(funding).Should().Be(
            funding.Summary.TotalSpendToDate,
            "the quarterly figures must add up to the total — BLUM-7607");

        // The deleted activity is gone from the editable list but keeps its own export row.
        funding.Activities.Select(a => a.ActivityId).Should().NotContain(removed.Id);
        var orphan = funding.UnreportedActivities.Should().ContainSingle().Subject;
        orphan.ActivityId.Should().Be(removed.Id);
        orphan.Title.Should().Contain("Removed activity");
        orphan.ActualByQuarter[1].Should().Be(100m);
        orphan.TotalActualToDate.Should().Be(100m);
    }

    // Planned activities were on both funding screens when BLUM-6612 shipped, came off both (BLUM-7504) and are back on
    // the forecast only (BLUM-7672), so historic actual spend against one is the shape most likely to be sitting in
    // live data. The activity has a forecast-only row in every quarter, Q4 included, and that row carries the earlier
    // money itself — never a "(no longer reported)" row, which would misdescribe an activity that is still live.
    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public async Task QuarterlyActuals_KeepSpendRecordedAgainstAnActivitySinceRevertedToPlanned_OnItsForecastOnlyRow(int reportingQuarter)
    {
        AuthenticateAsMainOrgLead();
        var activityId = await RecordQ1SpendThenRevertToPlannedAsync($"Reverted to planned Q1 (then Q{reportingQuarter})", 75m);

        var laterQuarter = await CreateOpenQuarterCollectionAsync(reportingQuarter, $"Reverted to planned Q{reportingQuarter}", CurrentFy);
        await CreateSubmissionAsync(laterQuarter);
        await ConfirmActivitiesAsync(laterQuarter);

        var funding = await GetFundingSpendAsync(laterQuarter);

        var row = funding.Activities.Should().ContainSingle(a => a.ActivityId == activityId).Subject;
        row.ForecastOnly.Should().BeTrue();
        row.ActualThisQuarter.Should().BeNull();
        row.ActualByQuarter[1].Should().Be(75m, "the Q1 money is history on the activity's own row");
        row.TotalActualToDate.Should().Be(75m);
        funding.UnreportedActivities.Should().BeEmpty("the activity still has a row, so nothing is orphaned");

        funding.TotalActualByQuarter[1].Should().Be(75m);
        funding.Summary.TotalSpendToDate.Should().Be(75m);
        SumOfQuarterlyActuals(funding).Should().Be(funding.Summary.TotalSpendToDate);
        (funding.Activities.Sum(a => a.TotalActualToDate) + funding.UnreportedActivities.Sum(a => a.TotalActualToDate) + funding.OtherTotalActualToDate)
            .Should().Be(funding.Summary.TotalSpendToDate, "each recorded amount must land in exactly one row");
    }

    // An activity reverted to Planned MID-report leaves the Actual row saved while it was active behind until the
    // actuals screen is next saved. Its row is forecast-only now, so "Activity spending" has no input for that money
    // and the frozen report must not count it — while its forecast counts like any other row's (BLUM-7672).
    [Fact]
    public async Task PlannedActivity_CountsInTheForecast_ButNotThisQuartersActual()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await CreateOpenQuarterCollectionAsync(1, "Planned mid-report Q1", CurrentFy);
        var active = await CreateRnaActivityAsync(title: "Active work");
        var reverted = await CreateRnaActivityAsync(title: "Reverted work");
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);
        (await SaveFundingActualsAsync(collectionId, saveAsDraft: true, otherAmount: 0m, (active.Id, 100m), (reverted.Id, 40m)))
            .EnsureSuccessStatusCode();

        await RevertToPlannedAsync(reverted.Id);

        var funding = await GetFundingSpendAsync(collectionId);
        var forecast = funding.Activities
            .Select(a => new FundingActivityForecastInputDto(
                a.ActivityId,
                funding.RemainingQuarters.Select(q => new FundingQuarterAmountInputDto(q.Quarter, 30m)).ToList()))
            .ToList();
        (await SaveFundingForecastAsync(collectionId, saveAsDraft: true, forecast, [])).EnsureSuccessStatusCode();

        var result = await GetFundingSpendAsync(collectionId);

        var row = result.Activities.Single(a => a.ActivityId == reverted.Id);
        row.ForecastOnly.Should().BeTrue();
        row.ActualThisQuarter.Should().BeNull("the stale £40 has no input on Activity spending");
        row.Forecast.Values.Should().OnlyContain(v => v == 30m);

        result.Summary.TotalSpendThisQuarter.Should().Be(100m);
        result.Summary.TotalSpendToDate.Should().Be(100m);
        result.Summary.TotalForecast.Should().Be(180m, "3 remaining quarters x 2 activities x £30, the Planned one included");
        result.Summary.ExpectedSpendForYear.Should().Be(280m);
        result.Summary.ExpectedSpendForYear.Should().Be(result.Summary.TotalSpendToDate + result.Summary.TotalForecast);
        result.UnreportedActivities.Should().BeEmpty();
    }

    [Fact]
    public async Task QuarterlyActuals_RenderAValue_WhenAQuartersOnlySpendIsOrphaned()
    {
        AuthenticateAsMainOrgLead();

        // The whole of Q1's spend sits on the activity that is later removed, so the Q1 column has nothing
        // reportable left to sum. It used to render blank while the money stayed inside the total.
        var q1 = await CreateOpenQuarterCollectionAsync(1, "Wholly orphaned Q1", CurrentFy);
        var activity = await CreateRnaActivityAsync(title: "Only activity");
        await CreateSubmissionAsync(q1);
        await ConfirmActivitiesAsync(q1);
        (await SaveFundingActualsAsync(q1, saveAsDraft: true, otherAmount: null, (activity.Id, 250m)))
            .EnsureSuccessStatusCode();

        (await Client.DeleteAsync($"activities/{activity.Id}")).EnsureSuccessStatusCode();

        var q2 = await CreateOpenQuarterCollectionAsync(2, "Wholly orphaned Q2", CurrentFy);
        await CreateSubmissionAsync(q2);
        await ConfirmActivitiesAsync(q2);

        var funding = await GetFundingSpendAsync(q2);

        funding.TotalActualByQuarter[1].Should().Be(250m, "blank would hide money the total still counts");
        funding.Summary.TotalSpendToDate.Should().Be(250m);
        SumOfQuarterlyActuals(funding).Should().Be(funding.Summary.TotalSpendToDate);
    }

    [Fact]
    public async Task FundingTotals_ReconcileWithTheirOwnLineItems()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await CreateOpenQuarterCollectionAsync(2, "Reconciliation Q2", CurrentFy);
        var activity = await CreateRnaActivityAsync();
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);
        await SeedCurrentWindowAllocationAsync(10_000m);
        await CompleteFundingSpendSectionAsync(collectionId);

        var funding = await GetFundingSpendAsync(collectionId);

        // The three identities the export depends on. Each held only by accident before BLUM-7607.
        SumOfQuarterlyActuals(funding).Should().Be(funding.Summary.TotalSpendToDate);
        funding.Summary.ExpectedSpendForYear.Should().Be(funding.Summary.TotalSpendToDate + funding.Summary.TotalForecast);

        var rowTotals = funding.Activities.Sum(a => a.TotalActualToDate)
            + funding.UnreportedActivities.Sum(a => a.TotalActualToDate)
            + funding.OtherTotalActualToDate;
        rowTotals.Should().Be(funding.Summary.TotalSpendToDate);

        // Each row's own "total actual to date" equals its own quarterly cells.
        var activityRow = funding.Activities.Single(a => a.ActivityId == activity.Id);
        activityRow.TotalActualToDate.Should().Be(
            activityRow.ActualByQuarter.Where(q => q.Key <= funding.CurrentQuarter).Sum(q => q.Value ?? 0m));
    }

    [Fact]
    public async Task ThisQuartersFigures_MatchTheScreen_WhenAnActivityIsDeletedMidReport()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await CreateOpenQuarterCollectionAsync(1, "Mid-report deletion Q1", CurrentFy);
        var kept = await CreateRnaActivityAsync(title: "Kept activity");
        var abandoned = await CreateRnaActivityAsync(title: "Abandoned activity");
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);
        (await SaveFundingActualsAsync(collectionId, saveAsDraft: true, otherAmount: 0m, (kept.Id, 60m), (abandoned.Id, 500m)))
            .EnsureSuccessStatusCode();

        // Deleting the activity mid-report leaves its £500 entry behind until the actuals screen is next saved. The
        // "Check your spending totals" screen can no longer show that money in any row, so the frozen report must not
        // count it either — otherwise the year total exceeds the rows above it with nothing to explain the difference.
        (await Client.DeleteAsync($"activities/{abandoned.Id}")).EnsureSuccessStatusCode();

        var funding = await GetFundingSpendAsync(collectionId);

        funding.Summary.TotalSpendThisQuarter.Should().Be(60m);
        funding.Summary.TotalSpendToDate.Should().Be(60m, "the reporting quarter counts exactly what the screen shows");
        funding.Summary.TotalSpendToDate.Should().Be(funding.Summary.SpendToDate + funding.Summary.TotalSpendThisQuarter);
        funding.Summary.ExpectedSpendForYear.Should().Be(
            funding.Summary.SpendToDate + funding.Summary.TotalSpendThisQuarter + funding.Summary.TotalForecast,
            "the summary box has to add up for the LA reading it");
        funding.UnreportedActivities.Should().BeEmpty("this quarter's own spend is still editable, so nothing is orphaned yet");
    }

    [Fact]
    public async Task PerActivityQuarterlyCells_IgnoreASiblingCollectionSharingTheSameQuarter()
    {
        AuthenticateAsMainOrgLead();
        var activity = await CreateRnaActivityAsync();

        // Two live Q1 collections for the same organisation. Each is a separate report and must describe only its own
        // answers — merging them made a row's ActualByQuarter disagree with its own ActualThisQuarter, and double-
        // counted the amount when the rows were summed across the window.
        var first = await CreateOpenQuarterCollectionAsync(1, "Sibling Q1 (a)", CurrentFy);
        await CreateSubmissionAsync(first);
        await ConfirmActivitiesAsync(first);
        (await SaveFundingActualsAsync(first, saveAsDraft: true, otherAmount: 0m, (activity.Id, 300m))).EnsureSuccessStatusCode();

        var second = await CreateOpenQuarterCollectionAsync(1, "Sibling Q1 (b)", CurrentFy);
        await CreateSubmissionAsync(second);
        await ConfirmActivitiesAsync(second);
        (await SaveFundingActualsAsync(second, saveAsDraft: true, otherAmount: 0m, (activity.Id, 40m))).EnsureSuccessStatusCode();

        var funding = await GetFundingSpendAsync(second);
        var row = funding.Activities.Single(a => a.ActivityId == activity.Id);

        row.ActualThisQuarter.Should().Be(40m);
        row.ActualByQuarter[1].Should().Be(40m, "the sibling collection's £300 belongs to its own report");
        row.TotalActualToDate.Should().Be(40m);
        funding.Summary.TotalSpendToDate.Should().Be(40m);
        SumOfQuarterlyActuals(funding).Should().Be(funding.Summary.TotalSpendToDate);
    }

    [Fact]
    public async Task UnreportedActivities_OmitAnActivityThatOnlyEverRecordedZero()
    {
        AuthenticateAsMainOrgLead();

        // £0 is an answer the service asks for explicitly, so most activities carry a zero row. A deleted one must not
        // leave a permanent all-zero "(no longer reported)" line in every future quarter's export — it reconciles
        // nothing and buries the rows that do carry money.
        var q1 = await CreateOpenQuarterCollectionAsync(1, "Zero orphan Q1", CurrentFy);
        var spent = await CreateRnaActivityAsync(title: "Spent activity");
        var zero = await CreateRnaActivityAsync(title: "Zero activity");
        await CreateSubmissionAsync(q1);
        await ConfirmActivitiesAsync(q1);
        (await SaveFundingActualsAsync(q1, saveAsDraft: true, otherAmount: 0m, (spent.Id, 80m), (zero.Id, 0m)))
            .EnsureSuccessStatusCode();

        (await Client.DeleteAsync($"activities/{spent.Id}")).EnsureSuccessStatusCode();
        (await Client.DeleteAsync($"activities/{zero.Id}")).EnsureSuccessStatusCode();

        var q2 = await CreateOpenQuarterCollectionAsync(2, "Zero orphan Q2", CurrentFy);
        await CreateSubmissionAsync(q2);
        await ConfirmActivitiesAsync(q2);

        var funding = await GetFundingSpendAsync(q2);

        funding.UnreportedActivities.Should().ContainSingle().Which.ActivityId.Should().Be(spent.Id);
        SumOfQuarterlyActuals(funding).Should().Be(funding.Summary.TotalSpendToDate);
    }

    // Q1-Q4 actual, treating a blank quarter as nothing recorded — the same arithmetic a DHSC analyst does across
    // the Q columns of funding-summary.csv.
    [Fact]
    public async Task TotalSpentSoFar_CarriesForwardSpendFromAnEarlierFinancialYear()
    {
        AuthenticateAsMainOrgLead();

        // The activity was reported against in the LAST financial year: £100 on the activity, £20 on "Other".
        var previousYearQ4 = await CreateOpenQuarterCollectionAsync(4, "Carry forward previous year Q4", PreviousFy);
        var activity = await CreateRnaActivityAsync();
        await CreateSubmissionAsync(previousYearQ4);
        await ConfirmActivitiesAsync(previousYearQ4);
        (await SaveFundingActualsAsync(previousYearQ4, saveAsDraft: true, otherAmount: 20m, (activity.Id, 100m)))
            .EnsureSuccessStatusCode();

        // The first report of the NEW financial year. Nothing has been spent in it yet.
        var q1 = await CreateOpenQuarterCollectionAsync(1, "Carry forward current year Q1", CurrentFy);
        await CreateSubmissionAsync(q1);
        await ConfirmActivitiesAsync(q1);

        var funding = await GetFundingSpendAsync(q1);
        var reported = funding.Activities.Single();

        reported.TotalSpentSoFar.Should().Be(
            100m,
            "the inset is total spent so far on the ACTIVITY, which outlives a financial year — before BLUM-7703 the "
            + "first report of a new year showed £0 on every activity, however much had been reported against them");

        // Everything the annual allocation is measured against stays scoped to THIS report's year.
        funding.Summary.SpendToDate.Should().Be(0m, "last year's spend is not this year's spend to date");
        funding.Summary.TotalSpendToDate.Should().Be(0m, "nothing has been spent in the new financial year yet");
        reported.TotalActualToDate.Should().Be(0m, "the export's per-activity total is this year only");
        reported.ActualByQuarter[4].Should().BeNull(
            "last year's Q4 must not appear as this year's Q4 column, or the export stops reconciling");
    }

    private static decimal SumOfQuarterlyActuals(FundingSpendDto funding) =>
        funding.TotalActualByQuarter.Values.Sum(v => v ?? 0m);

    private async Task<FundingDashboardDto> GetDashboardAsync()
    {
        var response = await Client.GetAsync("funding/dashboard");
        response.EnsureSuccessStatusCode();
        return await response.ToResponseModel<FundingDashboardDto>();
    }

    // Seeds an allocation for the current funding window (find-or-create the window so the resolver returns the same
    // one). The amount is the org's annual levy allocation surfaced as "Total grant allocation".
    private Task SeedCurrentWindowAllocationAsync(decimal amount) => SeedAllocationForFinancialYearAsync(CurrentFy, amount);

    // Seeds an allocation windowed to an EXPLICIT financial year, independent of CurrentFy — mirrors production,
    // where CreateLocalAuthority/BulkImportLocalAuthorities create exactly one OrgFundingAllocation row windowed to
    // whatever FY was "current" at onboarding, and it is never re-windowed by an amendment.
    private async Task SeedAllocationForFinancialYearAsync(string financialYear, decimal amount)
    {
        var window = await Context.FundingWindows.FirstOrDefaultAsync(w => w.FinancialYear == financialYear);
        if (window is null)
        {
            window = FundingWindow.New(FundingWindowId.New(), financialYear, $"Annual Allocation {financialYear}", DateTime.UtcNow);
            await Context.FundingWindows.AddAsync(window);
            await SaveChangesAsync();
        }

        await Context.OrgFundingAllocations.AddAsync(OrgFundingAllocation.New(
            OrgFundingAllocationId.New(), MainOrg.Id, window.Id, amount, UserId.New(), DateTime.UtcNow));
        await SaveChangesAsync();
    }

    // Records a Q1 actual against a fresh active activity, then flips it to Planned behind the API's back — the
    // activity editor would also flag open reports for review, which these tests are not about. Returns its id.
    private async Task<Guid> RecordQ1SpendThenRevertToPlannedAsync(string collectionName, decimal amount)
    {
        var q1 = await CreateOpenQuarterCollectionAsync(1, collectionName, CurrentFy);
        var activity = await CreateRnaActivityAsync(title: "Reverted activity");
        await CreateSubmissionAsync(q1);
        await ConfirmActivitiesAsync(q1);
        (await SaveFundingActualsAsync(q1, saveAsDraft: true, otherAmount: 0m, (activity.Id, amount)))
            .EnsureSuccessStatusCode();

        await RevertToPlannedAsync(activity.Id);
        return activity.Id;
    }

    private async Task RevertToPlannedAsync(Guid activityId)
    {
        await Context.Activities
            .Where(a => a.Id == new ActivityId(activityId))
            .ExecuteUpdateAsync(setter => setter.SetProperty(a => a.Status, ActivityStatus.Planned));
        Context.ChangeTracker.Clear();
    }
}