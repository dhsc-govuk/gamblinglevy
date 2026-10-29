using System.Net;
using System.Net.Http.Json;
using Api.Dtos;
using Domain.AuditLogs;
using Domain.Organisations;
using Domain.ValueObjects;
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
            AllocatedAmount: 250000m,
            ActivateImmediately: true);

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

    // BLUM-7608 AC1/AC2: the financial year is chosen rather than stamped from the clock, and a second
    // allocation for the upcoming year is written as its own row in its own window - the schema already keys
    // org_funding_allocations on (organisation_id, funding_window_id), so no new column was needed.
    [Fact]
    public async Task ShouldCreateBothAllocations_EachInItsOwnFinancialYearWindow()
    {
        var request = new CreateLocalAuthorityRequestDto(
            Name: "Two Year Council",
            OnsCode: "E06000101",
            Region: "London",
            Type: "LA",
            ECode: "E1000001",
            AllocatedAmount: 250_000m,
            FinancialYear: "2026/27",
            NextYearAllocatedAmount: 300_000m,
            ActivateImmediately: true);

        var response = await Client.PostAsJsonAsync("organisations/local-authorities", request);

        response.IsSuccessStatusCode.Should().BeTrue();
        var dto = await response.ToResponseModel<LocalAuthorityDto>();
        dto.AllocatedAmount.Should().Be(250_000m);
        dto.FundingFinancialYear.Should().Be("2026/27");
        dto.NextYearAllocatedAmount.Should().Be(300_000m);
        // Derived, never sent by the client: the upcoming year is always the year after the chosen one.
        dto.NextYearFinancialYear.Should().Be("2027/28");

        var org = await Context.Organisations.AsNoTracking().FirstAsync(x => x.Id == new OrganisationId(dto.Id));
        var allocations = await Context.OrgFundingAllocations.AsNoTracking()
            .Where(x => x.OrganisationId == org.Id)
            .Join(Context.FundingWindows.AsNoTracking(), a => a.FundingWindowId, w => w.Id, (a, w) => new { a.AllocatedAmount, w.FinancialYear })
            .ToListAsync();

        allocations.Should().HaveCount(2);
        allocations.Should().ContainSingle(x => x.FinancialYear == "2026/27" && x.AllocatedAmount == 250_000m);
        allocations.Should().ContainSingle(x => x.FinancialYear == "2027/28" && x.AllocatedAmount == 300_000m);
    }

    // The upcoming year's figure is optional: DHSC may not have agreed it at onboarding.
    [Fact]
    public async Task ShouldCreateOnlyOneAllocation_WhenNoUpcomingYearAmountIsGiven()
    {
        var request = new CreateLocalAuthorityRequestDto(
            Name: "One Year Council",
            OnsCode: "E06000102",
            Region: "London",
            Type: "LA",
            ECode: "E1000001",
            AllocatedAmount: 250_000m,
            FinancialYear: "2026/27",
            ActivateImmediately: true);

        var response = await Client.PostAsJsonAsync("organisations/local-authorities", request);

        response.IsSuccessStatusCode.Should().BeTrue();
        var dto = await response.ToResponseModel<LocalAuthorityDto>();
        dto.FundingFinancialYear.Should().Be("2026/27");
        dto.NextYearAllocatedAmount.Should().BeNull();
        dto.NextYearFinancialYear.Should().BeNull();

        var org = await Context.Organisations.AsNoTracking().FirstAsync(x => x.Id == new OrganisationId(dto.Id));
        (await Context.OrgFundingAllocations.AsNoTracking().CountAsync(x => x.OrganisationId == org.Id)).Should().Be(1);
    }

    // The year selects the funding window by exact string equality, so a malformed value would not fail - it
    // would create a window nothing else in the system matches. The property name in the envelope is what the
    // client's fieldNameMap routes on, so it is asserted rather than just the status.
    [Theory]
    [InlineData("2026")]
    [InlineData("2026/2027")]
    [InlineData("2026/28")]
    [InlineData("banana")]
    public async Task ShouldRejectAMalformedFinancialYear(string financialYear)
    {
        var request = new CreateLocalAuthorityRequestDto(
            Name: "Bad Year Council",
            OnsCode: "E06000103",
            Region: "London",
            Type: "LA",
            ECode: "E1000001",
            AllocatedAmount: 100m,
            FinancialYear: financialYear,
            ActivateImmediately: true);

        var response = await Client.PostAsJsonAsync("organisations/local-authorities", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("FinancialYear");
        (await Context.Organisations.AsNoTracking().AnyAsync(x => x.Name == "Bad Year Council")).Should().BeFalse();
    }

    [Fact]
    public async Task ShouldRejectANonPositiveUpcomingYearAllocation()
    {
        var request = new CreateLocalAuthorityRequestDto(
            Name: "Zero Next Year Council",
            OnsCode: "E06000104",
            Region: "London",
            Type: "LA",
            ECode: "E1000001",
            AllocatedAmount: 100m,
            FinancialYear: "2026/27",
            NextYearAllocatedAmount: 0m,
            ActivateImmediately: true);

        var response = await Client.PostAsJsonAsync("organisations/local-authorities", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("NextYearAllocatedAmount");
        (await Context.Organisations.AsNoTracking().AnyAsync(x => x.Name == "Zero Next Year Council")).Should().BeFalse();
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
            AllocatedAmount: 100m,
            ActivateImmediately: true);

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
            AllocatedAmount: 100m,
            ActivateImmediately: true);

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
            AllocatedAmount: 100m,
            ActivateImmediately: true);

        var response = await Client.PostAsJsonAsync("organisations/local-authorities", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Context.Organisations.AsNoTracking().AnyAsync(x => x.Name == "No Code Council")).Should().BeFalse();
    }

    // BLUM-7663 AC1/AC2: validated against the ONS reference list — in practice its shape, since no
    // enumerated list of real ONS codes exists (mirrors the Family Hubs project's OnsCodePattern). A
    // country-prefix letter (E, W, S or N) followed by eight digits; anything else is rejected with a
    // message telling the admin the code is not recognised.
    [Theory]
    [InlineData("E1000001")] // too few digits
    [InlineData("06000001")] // no letter prefix
    [InlineData("X06000001")] // disallowed leading letter
    public async Task ShouldRejectAMalformedOnsCode(string onsCode)
    {
        var request = new CreateLocalAuthorityRequestDto(
            Name: "Malformed Code Council",
            OnsCode: onsCode,
            Region: "London",
            Type: "LA",
            ECode: "E1000001",
            AllocatedAmount: 100m,
            ActivateImmediately: true);

        var response = await Client.PostAsJsonAsync("organisations/local-authorities", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("ONS code is not recognised. Enter it in the format E06000001 (a letter followed by 8 numbers).");
        (await Context.Organisations.AsNoTracking().AnyAsync(x => x.Name == "Malformed Code Council")).Should().BeFalse();
    }

    // BLUM-7663 AC3: a correctly-shaped code still saves with no errors.
    [Fact]
    public async Task ShouldCreateLocalAuthority_WithACorrectlyShapedOnsCode()
    {
        var request = new CreateLocalAuthorityRequestDto(
            Name: "Well Formed Council",
            OnsCode: "E06000201",
            Region: "London",
            Type: "LA",
            ECode: "E1000001",
            AllocatedAmount: 100m,
            ActivateImmediately: true);

        var response = await Client.PostAsJsonAsync("organisations/local-authorities", request);

        response.IsSuccessStatusCode.Should().BeTrue();
        var saved = await Context.Organisations.AsNoTracking().FirstAsync(x => x.Name == "Well Formed Council");
        saved.OnsCode.Should().Be("E06000201");
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
            AllocatedAmount: 100m,
            ActivateImmediately: true);

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
            Name: "First Charity", OnsCode: null, Region: "London", Type: "VCSE", ECode: "E1000001", AllocatedAmount: 100m, ActivateImmediately: true));
        var second = await Client.PostAsJsonAsync("organisations/local-authorities", new CreateLocalAuthorityRequestDto(
            Name: "Second Charity", OnsCode: null, Region: "London", Type: "VCSE", ECode: "E1000001", AllocatedAmount: 100m, ActivateImmediately: true));

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
            AllocatedAmount: 100m,
            ActivateImmediately: true);

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
            AllocatedAmount: 100m,
            ActivateImmediately: true);

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
            AllocatedAmount: 100m,
            ActivateImmediately: true);

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
            AllocatedAmount: 100m,
            ActivateImmediately: true);

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
            AllocatedAmount: 100m,
            ActivateImmediately: true);

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
            AllocatedAmount: 0m,
            ActivateImmediately: true);

        var response = await Client.PostAsJsonAsync("organisations/local-authorities", request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ShouldCreateAsInvited_WhenActivateItNowIsChosen()
    {
        var dto = await CreateAsync("Activated On Creation Council", "E06000106", activateImmediately: true);

        dto.OnboardingStatus.Should().Be(OnboardingStatuses.Invited);
        var saved = await Context.Organisations.AsNoTracking().FirstAsync(x => x.Id == new OrganisationId(dto.Id));
        saved.OnboardingStatus.ToString().Should().Be(OnboardingStatuses.Invited);
    }

    // "Activate it now" must leave the organisation deletable, as every new organisation was before BLUM-7945 - the
    // journey still offers "Delete account", and the delete command refuses an active organisation.
    [Fact]
    public async Task ShouldStillBeDeletable_WhenActivateItNowIsChosen()
    {
        var dto = await CreateAsync("Added By Mistake Council", "E06000110", activateImmediately: true);

        var response = await Client.DeleteAsync($"organisations/local-authorities/{dto.Id}");

        response.IsSuccessStatusCode.Should().BeTrue();
        (await Context.Organisations.AsNoTracking().AnyAsync(x => x.Id == new OrganisationId(dto.Id))).Should().BeFalse();
    }

    // The admin must pick one option: there is no default, because either one picked silently would decide whether
    // the organisation's users can sign in. An omitted answer is a 400 on the field, and nothing is created.
    [Fact]
    public async Task ShouldRejectAMissingActivationChoice()
    {
        var response = await Client.PostAsJsonAsync(
            "organisations/local-authorities",
            new CreateLocalAuthorityRequestDto(
                Name: "Undecided Council",
                OnsCode: "E06000109",
                Region: "London",
                Type: "LA",
                ECode: "E1000001",
                AllocatedAmount: 100m));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("ActivateImmediately").And.Contain("Select when you want to activate this account");
        (await Context.Organisations.AsNoTracking().AnyAsync(x => x.Name == "Undecided Council")).Should().BeFalse();
    }

    // "Leave it deactivated" is suspended, the state DHSC deactivating an organisation leaves (BLUM-7667), not
    // invited — invited blocks nobody. Nothing was ever active, so only the creation is audited.
    [Fact]
    public async Task ShouldCreateAsSuspended_WhenLeaveItDeactivatedIsChosen()
    {
        var dto = await CreateAsync("Deactivated On Creation Council", "E06000107", activateImmediately: false);

        dto.OnboardingStatus.Should().Be(OnboardingStatuses.Suspended);
        var saved = await Context.Organisations.AsNoTracking().FirstAsync(x => x.Id == new OrganisationId(dto.Id));
        saved.OnboardingStatus.ToString().Should().Be(OnboardingStatuses.Suspended);

        var audit = await Context.AuditLogs.AsNoTracking()
            .Where(x => x.TargetType == AuditTargetType.Organisation && x.TargetId == saved.Id.Value)
            .ToListAsync();
        audit.Should().ContainSingle().Which.Action.Should().Be(AuditAction.OrganisationCreated);
    }

    // The journey the client runs for "Leave it deactivated": the organisation comes back suspended, so its
    // contacts are created deactivated. Reactivating the organisation leaves them deactivated, exactly as after
    // a cascade deactivation (BLUM-7667 AC4) — each is reactivated individually.
    [Fact]
    public async Task ShouldKeepContactsDeactivated_WhenADeactivatedOnCreationOrganisationIsReactivated()
    {
        var org = await CreateAsync("Deactivated Contacts Council", "E06000108", activateImmediately: false);
        var contact = await Client.PostAsJsonAsync(
            $"organisations/{org.Id}/users/create",
            new UserDto(
                Id: null,
                FirstName: "Dee",
                LastName: "Activated",
                Email: ContactEmail,
                Status: "deactivated",
                Role: "lead",
                OrganisationName: null));
        contact.IsSuccessStatusCode.Should().BeTrue();

        var reinstate = await Client.PostAsync($"organisations/local-authorities/{org.Id}/reinstate", null);
        reinstate.StatusCode.Should().Be(HttpStatusCode.OK);

        var savedOrg = await Context.Organisations.AsNoTracking().FirstAsync(x => x.Id == new OrganisationId(org.Id));
        savedOrg.OnboardingStatus.ToString().Should().Be(OnboardingStatuses.Active);
        var user = await Context.Users.AsNoTracking().FirstAsync(x => x.Email == ContactEmail);
        user.Status.Should().Be(UserStatus.Deactivated);
    }

    private const string ContactEmail = "contact@deactivated-contacts-council.gov.uk";

    private async Task<LocalAuthorityDto> CreateAsync(string name, string onsCode, bool activateImmediately)
    {
        var response = await Client.PostAsJsonAsync(
            "organisations/local-authorities",
            new CreateLocalAuthorityRequestDto(
                Name: name,
                OnsCode: onsCode,
                Region: "London",
                Type: "LA",
                ECode: "E1000001",
                AllocatedAmount: 100m,
                ActivateImmediately: activateImmediately));

        response.IsSuccessStatusCode.Should().BeTrue();
        return await response.ToResponseModel<LocalAuthorityDto>();
    }

    public async Task InitializeAsync()
    {
        await Context.Organisations.AddAsync(_existing);
        await SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        Context.OrganisationUsers.RemoveRange(Context.OrganisationUsers.Where(x => x.User!.Email == ContactEmail));
        Context.Users.RemoveRange(Context.Users.Where(x => x.Email == ContactEmail));
        Context.AuditLogs.RemoveRange(Context.AuditLogs);
        Context.OrgFundingAllocations.RemoveRange(Context.OrgFundingAllocations);
        Context.FundingWindows.RemoveRange(Context.FundingWindows);
        Context.Organisations.RemoveRange(Context.Organisations);
        await SaveChangesAsync();
    }
}