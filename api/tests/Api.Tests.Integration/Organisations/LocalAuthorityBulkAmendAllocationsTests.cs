using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Api.Dtos;
using Domain.AuditLogs;
using Domain.Funding;
using Domain.Organisations;
using Domain.Users;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Tests.Common;
using Xunit;

namespace Api.Tests.Integration.Organisations;

public class LocalAuthorityBulkAmendAllocationsTests(IntegrationTestWebFactory factory)
    : BaseIntegrationTest(factory), IAsyncLifetime
{
    private readonly Organisation _orgA = Organisation.New(
        OrganisationId.New(), "Council A", "E06010001", Region.London, OrgType.LocalAuthority, "E1000001", OnboardingStatus.Active);
    private readonly Organisation _orgB = Organisation.New(
        OrganisationId.New(), "Council B", "E06010002", Region.London, OrgType.LocalAuthority, "E1000001", OnboardingStatus.Active);
    private readonly FundingWindow _window = FundingWindow.New(FundingWindowId.New(), "2025/26", "Annual 2025/26");

    [Fact]
    public async Task ShouldAmend_KnownRows_SkipUnknown_AndAuditLog()
    {
        var csv =
            "ons_code,allocated_amount\n" +
            "E06010001,300000\n" +
            "E06010002,400000\n" +
            "E06010099,500000\n";

        var result = await SendCsvAsync(csv);

        result.UpdatedCount.Should().Be(2);
        result.SkippedCount.Should().Be(1);
        result.FailedCount.Should().Be(0);
        result.Rows.Should().HaveCount(3);
        result.Rows.Should().ContainSingle(r => r.OnsCode == "E06010099" && r.Outcome == "Skipped");

        var orgAAllocation = await Context.OrgFundingAllocations.AsNoTracking().FirstAsync(x => x.OrganisationId == _orgA.Id);
        orgAAllocation.AllocatedAmount.Should().Be(300_000m);
        orgAAllocation.AmendedAt.Should().NotBeNull();

        var audit = await Context.AuditLogs.AsNoTracking()
            .Where(x => x.Action == AuditAction.OrganisationAllocationBulkAmended)
            .ToListAsync();
        audit.Should().ContainSingle();
        audit[0].After.Should().NotBeNullOrEmpty();
        audit[0].After.Should().Contain("E06010099");
    }

    [Fact]
    public async Task ShouldFailRow_WhenForbiddenColumnPresent()
    {
        var csv =
            "ons_code,allocated_amount,name\n" +
            "E06010001,300000,Council A\n";

        var result = await SendCsvAsync(csv);
        result.FailedCount.Should().Be(1);
        result.UpdatedCount.Should().Be(0);
        result.Rows[0].Reason.Should().Contain("Unsupported columns");
    }

    [Fact]
    public async Task ShouldFailRow_WhenAllocationMissing()
    {
        var orgC = Organisation.New(
            OrganisationId.New(), "Council C", "E06010003", Region.London, OrgType.LocalAuthority, "E1000001", OnboardingStatus.Invited);
        await Context.Organisations.AddAsync(orgC);
        await SaveChangesAsync();

        var csv =
            "ons_code,allocated_amount\n" +
            "E06010003,250000\n";

        var result = await SendCsvAsync(csv);
        result.FailedCount.Should().Be(1);
        result.Rows[0].Reason.Should().Contain("No existing allocation");
    }

    [Fact]
    public async Task ShouldBeIdempotent_OnRepeatedUpload()
    {
        var csv =
            "ons_code,allocated_amount\n" +
            "E06010001,300000\n";

        await SendCsvAsync(csv);
        var second = await SendCsvAsync(csv);
        second.UpdatedCount.Should().Be(1);

        var allocations = await Context.OrgFundingAllocations.AsNoTracking()
            .Where(x => x.OrganisationId == _orgA.Id).ToListAsync();
        allocations.Should().ContainSingle();
        allocations[0].AllocatedAmount.Should().Be(300_000m);
    }

    // Pins the boundary shift from case-normalising headers in the shared CsvParser (BLUM-7024): a
    // mixed-case forbidden column used to slip past the case-sensitive ContainsKey check and import; it is
    // now caught. This endpoint is not otherwise covered for mixed-case headers.
    [Fact]
    public async Task ShouldRejectRow_WhenForbiddenColumnPresent_InMixedCase()
    {
        var csv =
            "ons_code,allocated_amount,Name\n" +
            "E06010001,300000,Council A\n";

        var result = await SendCsvAsync(csv);

        result.FailedCount.Should().Be(1);
        result.UpdatedCount.Should().Be(0);
        result.Rows[0].Reason.Should().Contain("name");
    }

    [Fact]
    public async Task ShouldAmend_WhenHeadersAreMixedCase()
    {
        var csv =
            "ONS_Code,Allocated_Amount\n" +
            "E06010001,450000\n";

        var result = await SendCsvAsync(csv);

        result.UpdatedCount.Should().Be(1);
        var allocation = await Context.OrgFundingAllocations.AsNoTracking().FirstAsync(x => x.OrganisationId == _orgA.Id);
        allocation.AllocatedAmount.Should().Be(450_000m);
    }

    private async Task<BulkImportResultDto> SendCsvAsync(string csv)
    {
        using var content = new MultipartFormDataContent();
        var bytes = Encoding.UTF8.GetBytes(csv);
        var streamContent = new ByteArrayContent(bytes);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(streamContent, "file", "upload.csv");

        var response = await Client.PostAsync("organisations/local-authorities/bulk-allocations", content);
        response.IsSuccessStatusCode.Should().BeTrue();
        return (await response.Content.ReadFromJsonAsync<BulkImportResultDto>())!;
    }

    public async Task InitializeAsync()
    {
        await Context.Organisations.AddRangeAsync(_orgA, _orgB);
        await Context.FundingWindows.AddAsync(_window);
        await SaveChangesAsync();

        var allocA = OrgFundingAllocation.New(OrgFundingAllocationId.New(), _orgA.Id, _window.Id, 100_000m, UserId.New());
        var allocB = OrgFundingAllocation.New(OrgFundingAllocationId.New(), _orgB.Id, _window.Id, 100_000m, UserId.New());
        await Context.OrgFundingAllocations.AddRangeAsync(allocA, allocB);
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