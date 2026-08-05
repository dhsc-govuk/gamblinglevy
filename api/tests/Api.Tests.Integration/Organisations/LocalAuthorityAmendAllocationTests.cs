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
    private readonly FundingWindow _window = FundingWindow.New(FundingWindowId.New(), "2025/26", "Annual 2025/26");
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
            UserId.New());
        await Context.OrgFundingAllocations.AddAsync(_allocation);
        await SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        Context.AuditLogs.RemoveRange(Context.AuditLogs);
        Context.OrgFundingAllocations.RemoveRange(Context.OrgFundingAllocations);
        Context.FundingWindows.RemoveRange(Context.FundingWindows);
        Context.Organisations.RemoveRange(Context.Organisations);
        await SaveChangesAsync();
    }
}