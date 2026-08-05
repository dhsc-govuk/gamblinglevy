using System.Net;
using System.Net.Http.Json;
using Api.Dtos;
using Domain.AuditLogs;
using Domain.Organisations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Tests.Common;
using Tests.Data;
using Xunit;

namespace Api.Tests.Integration.Organisations;

public class LocalAuthorityCreateTests(IntegrationTestWebFactory factory)
    : BaseIntegrationTest(factory), IAsyncLifetime
{
    private readonly Organisation _existing = OrganisationsData.SecondOrganisation;

    [Fact]
    public async Task ShouldCreateLocalAuthority_WithAllocation_AndAuditLog()
    {
        var request = new CreateLocalAuthorityRequestDto(
            Name: "Brand New Council",
            OnsCode: "E06000099",
            Region: "London",
            Type: "LA",
            ECode: "E1000001",
            AllocatedAmount: 250000m);

        var response = await Client.PostAsJsonAsync("organisations/local-authorities", request);

        response.IsSuccessStatusCode.Should().BeTrue();
        var dto = await response.ToResponseModel<LocalAuthorityDto>();
        dto.Name.Should().Be("Brand New Council");
        dto.OnboardingStatus.Should().Be("invited");
        dto.AllocatedAmount.Should().Be(250000m);

        var org = await Context.Organisations.AsNoTracking().FirstAsync(x => x.Id == new OrganisationId(dto.Id));
        org.OnboardingStatus.ToString().Should().Be("invited");

        var allocation = await Context.OrgFundingAllocations.AsNoTracking().FirstAsync(x => x.OrganisationId == org.Id);
        allocation.AllocatedAmount.Should().Be(250000m);

        var window = await Context.FundingWindows.AsNoTracking().FirstAsync(x => x.Id == allocation.FundingWindowId);
        window.FinancialYear.Should().NotBeNullOrEmpty();

        var audit = await Context.AuditLogs.AsNoTracking()
            .Where(x => x.TargetType == AuditTargetType.Organisation && x.TargetId == org.Id.Value)
            .ToListAsync();
        audit.Should().ContainSingle(x => x.Action == AuditAction.OrganisationCreated);
    }

    [Fact]
    public async Task ShouldRejectDuplicateOnsCode()
    {
        var request = new CreateLocalAuthorityRequestDto(
            Name: "Some New Name",
            OnsCode: _existing.OnsCode,
            Region: "North West",
            Type: "LA",
            ECode: "E1000001",
            AllocatedAmount: 100m);

        var response = await Client.PostAsJsonAsync("organisations/local-authorities", request);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ShouldRejectDuplicateName()
    {
        var request = new CreateLocalAuthorityRequestDto(
            Name: _existing.Name,
            OnsCode: "E06000077",
            Region: "London",
            Type: "LA",
            ECode: "E1000001",
            AllocatedAmount: 100m);

        var response = await Client.PostAsJsonAsync("organisations/local-authorities", request);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // BLUM-7051: ons_code is mandatory for a Local Authority. Null and whitespace-only are both rejected by
    // the validator's NotEmpty rule.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ShouldRejectMissingOnsCode_ForALocalAuthority(string? onsCode)
    {
        var request = new CreateLocalAuthorityRequestDto(
            Name: "No Code Council",
            OnsCode: onsCode,
            Region: "London",
            Type: "LA",
            ECode: "E1000001",
            AllocatedAmount: 100m);

        var response = await Client.PostAsJsonAsync("organisations/local-authorities", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Context.Organisations.AsNoTracking().AnyAsync(x => x.Name == "No Code Council")).Should().BeFalse();
    }

    // The mirror image, and the reason the rule is conditional: an ONS code identifies a Local Authority
    // geography, so a VCSE organisation has none. Requiring it unconditionally would make VCSE organisations
    // impossible to create — and because ons_code is uniquely indexed, a placeholder would only work once.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task ShouldCreateVcseOrganisation_WithoutAnOnsCode(string? onsCode)
    {
        var request = new CreateLocalAuthorityRequestDto(
            Name: "Gambling Support Charity",
            OnsCode: onsCode,
            Region: "London",
            Type: "VCSE",
            ECode: "E1000001",
            AllocatedAmount: 100m);

        var response = await Client.PostAsJsonAsync("organisations/local-authorities", request);

        response.IsSuccessStatusCode.Should().BeTrue();

        var saved = await Context.Organisations.AsNoTracking().FirstAsync(x => x.Name == "Gambling Support Charity");
        saved.OnsCode.Should().BeNull();
        saved.Type.ToString().Should().Be("VCSE");
    }

    // Two VCSE organisations must be able to coexist. Before the rule was made conditional the only way past
    // the validator was a placeholder code, and the unique index then rejected the second one.
    [Fact]
    public async Task ShouldCreateMultipleVcseOrganisations_WithoutOnsCodes()
    {
        var first = await Client.PostAsJsonAsync("organisations/local-authorities", new CreateLocalAuthorityRequestDto(
            Name: "First Charity", OnsCode: null, Region: "London", Type: "VCSE", ECode: "E1000001", AllocatedAmount: 100m));
        var second = await Client.PostAsJsonAsync("organisations/local-authorities", new CreateLocalAuthorityRequestDto(
            Name: "Second Charity", OnsCode: null, Region: "London", Type: "VCSE", ECode: "E1000001", AllocatedAmount: 100m));

        first.IsSuccessStatusCode.Should().BeTrue();
        second.IsSuccessStatusCode.Should().BeTrue();
        (await Context.Organisations.AsNoTracking().CountAsync(x => x.OnsCode == null)).Should().Be(2);
    }

    // BLUM-7050: the E-code is mandatory. Unlike ons_code the rule carries no type condition, so the same
    // Theory has to hold for a VCSE organisation as for a Local Authority.
    [Theory]
    [InlineData("LA", "E06000055", null)]
    [InlineData("LA", "E06000056", "")]
    [InlineData("LA", "E06000057", "   ")]
    [InlineData("VCSE", null, null)]
    [InlineData("VCSE", null, "")]
    [InlineData("VCSE", null, "   ")]
    public async Task ShouldRejectMissingECode(string type, string? onsCode, string? eCode)
    {
        var request = new CreateLocalAuthorityRequestDto(
            Name: "No E-code Organisation",
            OnsCode: onsCode,
            Region: "London",
            Type: type,
            ECode: eCode,
            AllocatedAmount: 100m);

        var response = await Client.PostAsJsonAsync("organisations/local-authorities", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Context.Organisations.AsNoTracking().AnyAsync(x => x.Name == "No E-code Organisation")).Should().BeFalse();
    }

    [Fact]
    public async Task ShouldPersistTheECode_Trimmed()
    {
        var request = new CreateLocalAuthorityRequestDto(
            Name: "E-coded Council",
            OnsCode: "E06000058",
            Region: "London",
            Type: "LA",
            ECode: "  E1234567  ",
            AllocatedAmount: 100m);

        var response = await Client.PostAsJsonAsync("organisations/local-authorities", request);

        response.IsSuccessStatusCode.Should().BeTrue();
        var dto = await response.ToResponseModel<LocalAuthorityDto>();
        dto.ECode.Should().Be("E1234567");

        var saved = await Context.Organisations.AsNoTracking().FirstAsync(x => x.Id == new OrganisationId(dto.Id));
        saved.ECode.Should().Be("E1234567");
    }

    // BLUM-7050 review: the length rule is measured on the TRIMMED value, because that is what the handler
    // persists. A 64-character code pasted with surrounding whitespace fits the column and must be accepted.
    [Fact]
    public async Task ShouldAcceptAnECode_AtExactlyTheLengthLimit_IgnoringSurroundingWhitespace()
    {
        var maxLength = new string('E', 64);
        var request = new CreateLocalAuthorityRequestDto(
            Name: "Max Length Council",
            OnsCode: "E06000059",
            Region: "London",
            Type: "LA",
            ECode: $"  {maxLength}  ",
            AllocatedAmount: 100m);

        var response = await Client.PostAsJsonAsync("organisations/local-authorities", request);

        response.IsSuccessStatusCode.Should().BeTrue();
        var saved = await Context.Organisations.AsNoTracking().FirstAsync(x => x.Name == "Max Length Council");
        saved.ECode.Should().Be(maxLength);
    }

    // The message is set explicitly rather than left to FluentValidation's default, which would name the C#
    // property ("ECode") to the user in the error summary.
    [Fact]
    public async Task ShouldRejectAnECode_LongerThanTheLimit_WithUserFacingCopy()
    {
        var request = new CreateLocalAuthorityRequestDto(
            Name: "Over Long Council",
            OnsCode: "E06000060",
            Region: "London",
            Type: "LA",
            ECode: new string('E', 65),
            AllocatedAmount: 100m);

        var response = await Client.PostAsJsonAsync("organisations/local-authorities", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("E-code must be 64 characters or fewer");
        // The property name still has to be ECode so the client maps the error onto the field, but the human
        // copy must not read like FluentValidation's default ("The length of 'ECode' must be...").
        body.Should().NotContain("The length of");
        (await Context.Organisations.AsNoTracking().AnyAsync(x => x.Name == "Over Long Council")).Should().BeFalse();
    }

    // Case-insensitive duplicate detection: the DB unique index is case-sensitive, so this is enforced in
    // OrganisationRepository.FindByOnsCodeAsync and would otherwise let the same code in twice.
    [Fact]
    public async Task ShouldRejectDuplicateOnsCode_DifferingOnlyByCase()
    {
        var request = new CreateLocalAuthorityRequestDto(
            Name: "Lowercase Code Council",
            OnsCode: _existing.OnsCode!.ToLower(),
            Region: "London",
            Type: "LA",
            ECode: "E1000001",
            AllocatedAmount: 100m);

        var response = await Client.PostAsJsonAsync("organisations/local-authorities", request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ShouldRejectMissingAllocation()
    {
        var request = new CreateLocalAuthorityRequestDto(
            Name: "Unknown Council",
            OnsCode: "E06000076",
            Region: "London",
            Type: "LA",
            ECode: "E1000001",
            AllocatedAmount: 0m);

        var response = await Client.PostAsJsonAsync("organisations/local-authorities", request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    public async Task InitializeAsync()
    {
        await Context.Organisations.AddAsync(_existing);
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