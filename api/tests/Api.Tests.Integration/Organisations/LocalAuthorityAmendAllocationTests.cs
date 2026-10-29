using System.Net;
using System.Net.Http.Json;
using Api.Dtos;
using Domain.AuditLogs;
using Domain.Funding;
using Domain.Organisations;
using Domain.Users;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Tests.Common;
using Tests.Data;
using Xunit;

namespace Api.Tests.Integration.Organisations;

public class LocalAuthorityAmendAllocationTests(IntegrationTestWebFactory factory)
    : BaseIntegrationTest(factory), IAsyncLifetime
{
    private readonly Organisation _target = OrganisationsData.SecondOrganisation;
    private readonly FundingWindow _window = FundingWindow.New(FundingWindowId.New(), "2025/26", "Annual 2025/26", DateTime.UtcNow);
    private OrgFundingAllocation _allocation = null!;

    [Fact]
    public async Task ShouldAmend_AllocationAmount_AndRecordAuditLog()
    {
        var request = new AmendLocalAuthorityAllocationRequestDto(750_000m);

        var response = await Client.PatchAsJsonAsync(
            $"organisations/local-authorities/{_target.Id.Value}/allocation",
            request);

        response.IsSuccessStatusCode.Should().BeTrue();
        var dto = await response.ToResponseModel<LocalAuthorityDto>();
        dto.AllocatedAmount.Should().Be(750_000m);

        var saved = await Context.OrgFundingAllocations.AsNoTracking().FirstAsync(x => x.Id == _allocation.Id);
        saved.AllocatedAmount.Should().Be(750_000m);
        saved.AmendedAt.Should().NotBeNull();
        saved.AmendedBy.Should().NotBeNull();

        var audit = await Context.AuditLogs.AsNoTracking()
            .Where(x => x.TargetType == AuditTargetType.Organisation && x.TargetId == _target.Id.Value)
            .ToListAsync();
        audit.Should().ContainSingle(x => x.Action == AuditAction.OrganisationAllocationAmended);
        audit[0].Before.Should().Contain("100000");
        audit[0].After.Should().Contain("750000");
    }

    // BLUM-7608: amending is now addressed by financial year, and it UPSERTS. Naming a year the organisation
    // has no row for adds one instead of failing.
    [Fact]
    public async Task ShouldCreateAllocationsForBothYears_WhenNeitherExistsYet()
    {
        var request = new AmendLocalAuthorityAllocationRequestDto(
            AllocatedAmount: 200_000m,
            FinancialYear: "2030/31",
            NextYearAllocatedAmount: 300_000m);

        var response = await Client.PatchAsJsonAsync(
            $"organisations/local-authorities/{_target.Id.Value}/allocation",
            request);

        response.IsSuccessStatusCode.Should().BeTrue();
        var dto = await response.ToResponseModel<LocalAuthorityDto>();
        dto.AllocatedAmount.Should().Be(200_000m);
        dto.FundingFinancialYear.Should().Be("2030/31");
        dto.NextYearAllocatedAmount.Should().Be(300_000m);
        dto.NextYearFinancialYear.Should().Be("2031/32");

        var rows = await AllocationsByFinancialYearAsync();
        rows["2030/31"].Should().Be(200_000m);
        rows["2031/32"].Should().Be(300_000m);
    }

    // The second amend of the same year must update the row rather than adding a duplicate - the unique index on
    // (organisation_id, funding_window_id) would refuse it anyway, so this proves the upsert branch is taken.
    [Fact]
    public async Task ShouldUpdateTheSameRows_WhenAmendedTwiceForTheSameYear()
    {
        var route = $"organisations/local-authorities/{_target.Id.Value}/allocation";

        (await Client.PatchAsJsonAsync(route, new AmendLocalAuthorityAllocationRequestDto(10m, "2030/31", 20m)))
            .EnsureSuccessStatusCode();
        (await Client.PatchAsJsonAsync(route, new AmendLocalAuthorityAllocationRequestDto(30m, "2030/31", 40m)))
            .EnsureSuccessStatusCode();

        var rows = await AllocationsByFinancialYearAsync();
        rows["2030/31"].Should().Be(30m);
        rows["2031/32"].Should().Be(40m);
        (await Context.OrgFundingAllocations.AsNoTracking().CountAsync(x => x.OrganisationId == _target.Id)).Should().Be(3);
    }

    // Deliberately NOT "move the existing row onto the new year": the 2025/26 figure is what every submission
    // frozen in 2025/26 was measured against, so re-labelling it would rewrite history.
    [Fact]
    public async Task ShouldLeaveEarlierYearsUntouched_WhenALaterYearIsAmended()
    {
        var response = await Client.PatchAsJsonAsync(
            $"organisations/local-authorities/{_target.Id.Value}/allocation",
            new AmendLocalAuthorityAllocationRequestDto(999m, "2030/31"));

        response.IsSuccessStatusCode.Should().BeTrue();

        var rows = await AllocationsByFinancialYearAsync();
        rows["2025/26"].Should().Be(100_000m, "the year that was already recorded must not be re-labelled or overwritten");
        rows["2030/31"].Should().Be(999m);
    }

    // No financial year supplied keeps the pre-BLUM-7608 behaviour exactly: amend whichever year the
    // organisation's existing allocation already names.
    [Fact]
    public async Task ShouldAmendTheExistingYear_WhenNoFinancialYearIsSupplied()
    {
        var response = await Client.PatchAsJsonAsync(
            $"organisations/local-authorities/{_target.Id.Value}/allocation",
            new AmendLocalAuthorityAllocationRequestDto(555m));

        response.IsSuccessStatusCode.Should().BeTrue();
        var dto = await response.ToResponseModel<LocalAuthorityDto>();
        dto.FundingFinancialYear.Should().Be("2025/26");

        (await AllocationsByFinancialYearAsync())["2025/26"].Should().Be(555m);
    }

    // Regression: the first cut resolved "the organisation's existing allocation" with
    // FindLatestForOrganisationAsync, which orders by CreatedAt. Since the upcoming year's row is written
    // second, "latest" was NEXT year — so a PATCH with no financial year amended the wrong one, and the audit
    // "before" described the wrong one too. Seeded through the real create endpoint so the CreatedAt ordering
    // that caused it is genuinely reproduced.
    [Fact]
    public async Task ShouldAmendTheYearInProgress_NotTheUpcomingYear_WhenTheOrganisationHoldsBoth()
    {
        var currentFy = TestFinancialYears.Current;
        var upcomingFy = TestFinancialYears.Next(currentFy);

        var created = await Client.PostAsJsonAsync("organisations/local-authorities", new CreateLocalAuthorityRequestDto(
            Name: "Two Allocation Council",
            OnsCode: "E06000200",
            Region: "London",
            Type: "LA",
            ECode: "E1000001",
            AllocatedAmount: 100_000m,
            FinancialYear: currentFy,
            NextYearAllocatedAmount: 120_000m,
            ActivateImmediately: true));
        created.EnsureSuccessStatusCode();
        var organisationId = (await created.ToResponseModel<LocalAuthorityDto>()).Id;

        var response = await Client.PatchAsJsonAsync(
            $"organisations/local-authorities/{organisationId}/allocation",
            new AmendLocalAuthorityAllocationRequestDto(150_000m));

        response.IsSuccessStatusCode.Should().BeTrue();
        var dto = await response.ToResponseModel<LocalAuthorityDto>();
        dto.FundingFinancialYear.Should().Be(currentFy);
        dto.AllocatedAmount.Should().Be(150_000m);

        var rows = await AllocationsByFinancialYearAsync(new OrganisationId(organisationId));
        rows[currentFy].Should().Be(150_000m);
        rows[upcomingFy].Should().Be(120_000m, "the upcoming year must be untouched");

        // The audit "before" has to describe the year that actually changed, or the trail reads as if next
        // year's allocation had been relabelled.
        var audit = await Context.AuditLogs.AsNoTracking()
            .Where(x => x.TargetId == organisationId && x.Action == AuditAction.OrganisationAllocationAmended)
            .SingleAsync();
        audit.Before.Should().Contain(currentFy).And.Contain("100000");
        audit.After.Should().Contain("150000");
    }

    // Omitting the upcoming year's amount means "leave it alone" — the amend screen's hint says so. The response
    // and the audit "after" must still describe the row that is there, or they read as a removal.
    [Fact]
    public async Task ShouldReportTheUntouchedUpcomingYear_WhenNoUpcomingAmountIsSupplied()
    {
        var route = $"organisations/local-authorities/{_target.Id.Value}/allocation";
        (await Client.PatchAsJsonAsync(route, new AmendLocalAuthorityAllocationRequestDto(10m, "2030/31", 20m)))
            .EnsureSuccessStatusCode();

        var response = await Client.PatchAsJsonAsync(route, new AmendLocalAuthorityAllocationRequestDto(30m, "2030/31"));

        response.IsSuccessStatusCode.Should().BeTrue();
        var dto = await response.ToResponseModel<LocalAuthorityDto>();
        dto.NextYearAllocatedAmount.Should().Be(20m);
        dto.NextYearFinancialYear.Should().Be("2031/32");

        (await AllocationsByFinancialYearAsync())["2031/32"].Should().Be(20m);
    }

    [Fact]
    public async Task ShouldRejectAMalformedFinancialYear()
    {
        var response = await Client.PatchAsJsonAsync(
            $"organisations/local-authorities/{_target.Id.Value}/allocation",
            new AmendLocalAuthorityAllocationRequestDto(100m, "2026/2027"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Context.FundingWindows.AsNoTracking().AnyAsync(x => x.FinancialYear == "2026/2027")).Should().BeFalse();
    }

    [Fact]
    public async Task ShouldReturnNotFound_WhenOrganisationMissing()
    {
        var request = new AmendLocalAuthorityAllocationRequestDto(500m);
        var response = await Client.PatchAsJsonAsync(
            $"organisations/local-authorities/{Guid.NewGuid()}/allocation",
            request);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ShouldReturnBadRequest_WhenAmountIsZeroOrNegative()
    {
        var request = new AmendLocalAuthorityAllocationRequestDto(0m);
        var response = await Client.PatchAsJsonAsync(
            $"organisations/local-authorities/{_target.Id.Value}/allocation",
            request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ShouldReturnNotFound_WhenAllocationMissing()
    {
        Context.OrgFundingAllocations.Remove(_allocation);
        await SaveChangesAsync();

        var request = new AmendLocalAuthorityAllocationRequestDto(500m);
        var response = await Client.PatchAsJsonAsync(
            $"organisations/local-authorities/{_target.Id.Value}/allocation",
            request);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private Task<Dictionary<string, decimal>> AllocationsByFinancialYearAsync() => AllocationsByFinancialYearAsync(_target.Id);

    private async Task<Dictionary<string, decimal>> AllocationsByFinancialYearAsync(OrganisationId organisationId) =>
        await Context.OrgFundingAllocations.AsNoTracking()
            .Where(x => x.OrganisationId == organisationId)
            .Join(Context.FundingWindows.AsNoTracking(), a => a.FundingWindowId, w => w.Id, (a, w) => new { w.FinancialYear, a.AllocatedAmount })
            .ToDictionaryAsync(x => x.FinancialYear, x => x.AllocatedAmount);

    public async Task InitializeAsync()
    {
        await Context.Organisations.AddAsync(_target);
        await Context.FundingWindows.AddAsync(_window);
        await SaveChangesAsync();

        _allocation = OrgFundingAllocation.New(
            OrgFundingAllocationId.New(),
            _target.Id,
            _window.Id,
            100_000m,
            UserId.New(),
            DateTime.UtcNow);
        await Context.OrgFundingAllocations.AddAsync(_allocation);
        await SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        Context.AuditLogs.RemoveRange(Context.AuditLogs);
        Context.Organisations.RemoveRange(Context.Organisations);
        Context.OrgFundingAllocations.RemoveRange(Context.OrgFundingAllocations);
        Context.FundingWindows.RemoveRange(Context.FundingWindows);
        Context.Organisations.RemoveRange(Context.Organisations);
        await SaveChangesAsync();
    }
}