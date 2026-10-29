using Api.Dtos;
using Domain.Funding;
using Domain.Organisations;
using Domain.Users;
using Domain.ValueObjects;
using FluentAssertions;
using Tests.Common;
using Tests.Data;
using Xunit;

namespace Api.Tests.Integration.Funding;

// Verifies the LA funding dashboard (BLUM-6213): annual allocation for the current funding window,
// cumulative approved spend (currently a 0 stub — SpendReport not built yet), remaining budget, and
// that figures are scoped to the caller's own organisation.
public class FundingDashboardTests(IntegrationTestWebFactory factory)
    : BaseIntegrationTest(factory), IAsyncLifetime
{
    private static readonly Organisation MainOrg = OrganisationsData.MainOrganisation;
    private static readonly Organisation SecondOrg = OrganisationsData.SecondOrganisation;

    private static readonly Guid MainOrgLeadUserId = Guid.Parse("c0000000-0000-0000-0000-000000000001");
    private static readonly Guid SecondOrgLeadUserId = Guid.Parse("c0000000-0000-0000-0000-000000000002");

    private static readonly string CurrentFy = TestFinancialYears.Current;

    private readonly FundingWindow _currentWindow =
        FundingWindow.New(FundingWindowId.New(), CurrentFy, $"Annual Allocation {CurrentFy}", DateTime.UtcNow);

    [Fact]
    public async Task GetDashboard_ReturnsAllocation_SpendZero_AndRemaining_ForCurrentWindow()
    {
        await SeedAllocationAsync(MainOrg, 150_000m);
        AuthenticateAs(Role.Lead, MainOrgLeadUserId, MainOrg.Id.Value);

        var response = await Client.GetAsync("funding/dashboard");

        response.IsSuccessStatusCode.Should().BeTrue();
        var dto = await response.ToResponseModel<FundingDashboardDto>();
        dto.AllocatedAmount.Should().Be(150_000m);
        dto.CumulativeSpend.Should().Be(0m);
        dto.RemainingBudget.Should().Be(150_000m);
        dto.FinancialYear.Should().Be(CurrentFy);
        dto.HasAllocation.Should().BeTrue();
    }

    [Fact]
    public async Task GetDashboard_ReturnsZeroes_WhenNoAllocationForCurrentWindow()
    {
        AuthenticateAs(Role.Lead, MainOrgLeadUserId, MainOrg.Id.Value);

        var response = await Client.GetAsync("funding/dashboard");

        response.IsSuccessStatusCode.Should().BeTrue();
        var dto = await response.ToResponseModel<FundingDashboardDto>();
        dto.AllocatedAmount.Should().Be(0m);
        dto.CumulativeSpend.Should().Be(0m);
        dto.RemainingBudget.Should().Be(0m);
        dto.HasAllocation.Should().BeFalse();
    }

    [Fact]
    public async Task GetDashboard_IsScopedToCallersOrganisation()
    {
        await SeedAllocationAsync(MainOrg, 150_000m);

        // A user from a different organisation must not see MainOrg's allocation.
        AuthenticateAs(Role.Lead, SecondOrgLeadUserId, SecondOrg.Id.Value);

        var response = await Client.GetAsync("funding/dashboard");

        response.IsSuccessStatusCode.Should().BeTrue();
        var dto = await response.ToResponseModel<FundingDashboardDto>();
        dto.AllocatedAmount.Should().Be(0m);
        dto.HasAllocation.Should().BeFalse();
    }

    // BLUM-7608 made the financial year something the admin chooses rather than something the clock stamps, so
    // an LA onboarded in, say, February for the year starting in April holds only that later year. A bare
    // current-window lookup showed it "£0, no allocation set" until April — the money is recorded, the LA just
    // could not see it.
    [Fact]
    public async Task GetDashboard_ShowsAnAllocationHeldOnlyForTheUpcomingYear()
    {
        var upcomingFy = TestFinancialYears.Next(CurrentFy);
        var upcomingWindow = FundingWindow.New(FundingWindowId.New(), upcomingFy, $"Annual Allocation {upcomingFy}", DateTime.UtcNow);
        await Context.FundingWindows.AddAsync(upcomingWindow);
        await SaveChangesAsync();

        await Context.OrgFundingAllocations.AddAsync(OrgFundingAllocation.New(
            OrgFundingAllocationId.New(), MainOrg.Id, upcomingWindow.Id, 90_000m, UserId.New(), DateTime.UtcNow));
        await SaveChangesAsync();

        AuthenticateAs(Role.Lead, MainOrgLeadUserId, MainOrg.Id.Value);

        var response = await Client.GetAsync("funding/dashboard");

        response.IsSuccessStatusCode.Should().BeTrue();
        var dto = await response.ToResponseModel<FundingDashboardDto>();
        dto.AllocatedAmount.Should().Be(90_000m);
        dto.HasAllocation.Should().BeTrue();
        // Labelled with the year it is actually for, so the tile never implies the money belongs to this year.
        dto.FinancialYear.Should().Be(upcomingFy);
    }

    // The year in progress still wins over a later one when both exist — the fall-forward above is a fallback,
    // not a preference.
    [Fact]
    public async Task GetDashboard_PrefersTheYearInProgress_WhenBothYearsAreHeld()
    {
        await SeedAllocationAsync(MainOrg, 150_000m);

        var upcomingFy = TestFinancialYears.Next(CurrentFy);
        var upcomingWindow = FundingWindow.New(FundingWindowId.New(), upcomingFy, $"Annual Allocation {upcomingFy}", DateTime.UtcNow);
        await Context.FundingWindows.AddAsync(upcomingWindow);
        await SaveChangesAsync();
        await Context.OrgFundingAllocations.AddAsync(OrgFundingAllocation.New(
            OrgFundingAllocationId.New(), MainOrg.Id, upcomingWindow.Id, 90_000m, UserId.New(), DateTime.UtcNow));
        await SaveChangesAsync();

        AuthenticateAs(Role.Lead, MainOrgLeadUserId, MainOrg.Id.Value);

        var dto = await (await Client.GetAsync("funding/dashboard")).ToResponseModel<FundingDashboardDto>();

        dto.AllocatedAmount.Should().Be(150_000m);
        dto.FinancialYear.Should().Be(CurrentFy);
    }

    private async Task SeedAllocationAsync(Organisation organisation, decimal amount)
    {
        await Context.OrgFundingAllocations.AddAsync(OrgFundingAllocation.New(
            OrgFundingAllocationId.New(),
            organisation.Id,
            _currentWindow.Id,
            amount,
            UserId.New(),
            DateTime.UtcNow));
        await SaveChangesAsync();
    }

    public async Task InitializeAsync()
    {
        AuthenticateAsAdmin();
        await Context.Organisations.AddAsync(MainOrg);
        await Context.Organisations.AddAsync(SecondOrg);
        await Context.FundingWindows.AddAsync(_currentWindow);
        await SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        Context.OrgFundingAllocations.RemoveRange(Context.OrgFundingAllocations);
        Context.FundingWindows.RemoveRange(Context.FundingWindows);
        Context.Organisations.RemoveRange(Context.Organisations);
        await SaveChangesAsync();
        AuthenticateAsAdmin();
    }
}