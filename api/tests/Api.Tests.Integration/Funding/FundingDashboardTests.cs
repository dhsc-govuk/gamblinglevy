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

    private static readonly string CurrentFy = ComputeCurrentFinancialYear();

    private readonly FundingWindow _currentWindow =
        FundingWindow.New(FundingWindowId.New(), CurrentFy, $"Annual Allocation {CurrentFy}");

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

    private async Task SeedAllocationAsync(Organisation organisation, decimal amount)
    {
        await Context.OrgFundingAllocations.AddAsync(OrgFundingAllocation.New(
            OrgFundingAllocationId.New(),
            organisation.Id,
            _currentWindow.Id,
            amount,
            UserId.New()));
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

    // Mirrors FundingWindowResolver.CurrentFinancialYear so the seeded window matches the one the
    // endpoint resolves at test runtime (UK financial year boundary: 6 April).
    private static string ComputeCurrentFinancialYear()
    {
        var now = DateTime.UtcNow;
        var fyStartYear = (now.Month > 4 || (now.Month == 4 && now.Day >= 6)) ? now.Year : now.Year - 1;
        return $"{fyStartYear}/{(fyStartYear + 1) % 100:D2}";
    }
}