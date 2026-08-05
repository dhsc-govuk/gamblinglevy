using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Api.Dtos;
using Application.Common.Validation;
using Domain.Activities;
using Domain.AuditLogs;
using Domain.Organisations;
using Domain.ValueObjects;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Tests.Common;
using Tests.Data;
using Xunit;

namespace Api.Tests.Integration.Security;

/// <summary>
/// BLUM-7240 / CWE-20 — server-side allow-list input validation.
///
/// The Nanorisk NR-4099 pen test found that Azure Front Door blocks the well-known injection strings while
/// injection characters pass straight through to the application layer. These cases are the evidence for the
/// story's acceptance criteria, kept together deliberately: each is a property of the request boundary rather
/// than of any aggregate, so scattering them across the per-aggregate classes would leave a reviewer with no
/// way to see the matrix.
///
/// The envelope case matters most. Two client files parse that shape — views/Admin/Collections/messages.ts
/// reads errors[0].message, and views/Admin/Organisations/errors.ts keys its GDS copy off the C# property
/// name in `property` — and until now nothing pinned it.
/// </summary>
public class InputValidationTests(IntegrationTestWebFactory factory)
    : BaseIntegrationTest(factory), IAsyncLifetime
{
    private const string LocalAuthoritiesRoute = "organisations/local-authorities";
    private const string CollectionsRoute = "data-collections";
    private const string AuditLogsRoute = "admin/audit-logs";

    private static readonly Organisation Org = OrganisationsData.MainOrganisation;
    private static readonly Guid OrgLeadUserId = Guid.Parse("bb000000-0000-0000-0000-000000000001");

    // ---- The envelope (the contract two client files depend on) -------------------------------------

    [Fact]
    public async Task ShouldAnswerFluentValidationFailure_WithTheValidationFailedEnvelope()
    {
        var request = ValidLocalAuthority() with { Name = string.Empty };

        var response = await Client.PostAsJsonAsync(LocalAuthoritiesRoute, request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var (error, errors) = await ReadEnvelopeAsync(response);
        error.Should().Be("validation_failed");

        // The C# property name, not the JSON name — errors.ts looks this up in its fieldNameMap.
        errors.Should().Contain(e => e.Property == "Name" && !string.IsNullOrWhiteSpace(e.Message));
    }

    [Fact]
    public async Task ShouldAnswerModelBindingFailure_WithTheSameEnvelope_AndNoInternalDetail()
    {
        var response = await PostJsonAsync(LocalAuthoritiesRoute, LocalAuthorityJson(allocatedAmount: Quoted("not-a-number")));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var rawBody = await response.Content.ReadAsStringAsync();
        var (error, errors) = await ReadEnvelopeAsync(response);

        error.Should().Be("validation_failed");
        errors.Should().Contain(e => e.Property == "AllocatedAmount");

        // System.Text.Json's own message names the CLR type, the JSON path and the byte position, and used
        // to be handed straight to the caller.
        rawBody.Should().NotContain("System.");
        rawBody.Should().NotContain("Path:");
        rawBody.Should().NotContain("LineNumber");
        rawBody.Should().NotContain("BytePositionInLine");
    }

    // ---- AC1: wrong data type ----------------------------------------------------------------------

    [Fact]
    public async Task ShouldReject_WhenANumberIsSentAsAString()
    {
        // Accepted before BLUM-7240: JsonSerializerDefaults.Web sets NumberHandling to
        // AllowReadingFromString, so a quoted number bound silently.
        var response = await PostJsonAsync(LocalAuthoritiesRoute, LocalAuthorityJson(allocatedAmount: Quoted("250000")));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertNoOrganisationCreatedAsync();
    }

    // ---- AC2: string exceeding max length -----------------------------------------------------------

    [Fact]
    public async Task ShouldReject_WhenNameExceedsItsColumnWidth()
    {
        var request = ValidLocalAuthority() with { Name = new string('a', FieldLimits.Name + 1) };

        var response = await Client.PostAsJsonAsync(LocalAuthoritiesRoute, request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertNoOrganisationCreatedAsync();
    }

    [Fact]
    public async Task ShouldReject_WhenFinancialYearExceedsItsColumnWidth()
    {
        // collections.financial_year is varchar(16). The validator allowed 20, so a 17 to 20 character value
        // passed validation and then raised 22001 on SaveChanges as a 500.
        var overLong = new string('9', FieldLimits.FinancialYear + 1);
        var json = OpenBrace + Field("name", "Over-long year") + Comma + Field("financialYear", overLong)
            + Comma + RawField("saveAsDraft", "true") + CloseBrace;

        var response = await PostJsonAsync(CollectionsRoute, json);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Context.Collections.AsNoTracking().AnyAsync()).Should().BeFalse();
    }

    // ---- AC3: numeric value outside its range -------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ShouldReject_WhenAllocationIsNotPositive(int amount)
    {
        var request = ValidLocalAuthority() with { AllocatedAmount = amount };

        var response = await Client.PostAsJsonAsync(LocalAuthoritiesRoute, request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertNoOrganisationCreatedAsync();
    }

    [Fact]
    public async Task ShouldReject_WhenAllocationExceedsTheNumericColumn()
    {
        // numeric(18,2) holds sixteen integer digits; anything larger raised 22003 as a 500.
        var request = ValidLocalAuthority() with { AllocatedAmount = FieldLimits.MaxMoney + 1m };

        var response = await Client.PostAsJsonAsync(LocalAuthoritiesRoute, request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertNoOrganisationCreatedAsync();
    }

    [Fact]
    public async Task ShouldReject_WhenAllocationHasMoreThanTwoDecimalPlaces()
    {
        // Previously rounded on the way into numeric(18,2) without telling anyone.
        var request = ValidLocalAuthority() with { AllocatedAmount = 1.005m };

        var response = await Client.PostAsJsonAsync(LocalAuthoritiesRoute, request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertNoOrganisationCreatedAsync();
    }

    [Theory]
    [InlineData(0, 50)]
    [InlineData(-1, 50)]
    [InlineData(2000000000, 50)]
    [InlineData(1, 0)]
    [InlineData(1, FieldLimits.MaxPageSize + 1)]
    public async Task ShouldReject_WhenPagingIsOutOfRange(int page, int pageSize)
    {
        // Previously answered 200 OK with a silently clamped page — and a large enough page number overflowed
        // the int multiplication behind Skip() into a negative OFFSET.
        var response = await Client.GetAsync(AuditLogsRoute + "?page=" + page + "&pageSize=" + pageSize);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var (error, _) = await ReadEnvelopeAsync(response);
        error.Should().Be("validation_failed");
    }

    [Fact]
    public async Task ShouldAcceptPaging_AtTheUpperBoundary()
    {
        var response = await Client.GetAsync(AuditLogsRoute + "?page=1&pageSize=" + FieldLimits.MaxPageSize);

        response.IsSuccessStatusCode.Should().BeTrue();
    }

    // ---- AC4: unlisted enumeration value ------------------------------------------------------------

    [Fact]
    public async Task ShouldReject_WhenRegionIsNotSupported()
    {
        var request = ValidLocalAuthority() with { Region = "Atlantis" };

        var response = await Client.PostAsJsonAsync(LocalAuthoritiesRoute, request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertNoOrganisationCreatedAsync();
    }

    [Fact]
    public async Task ShouldReject_WhenTypeIsNotSupported()
    {
        var request = ValidLocalAuthority() with { Type = "SUPERUSER" };

        var response = await Client.PostAsJsonAsync(LocalAuthoritiesRoute, request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertNoOrganisationCreatedAsync();
    }

    [Fact]
    public async Task ShouldReject_WhenOnboardingStatusFilterIsNotSupported()
    {
        // Reached OnboardingStatus.From inside the repository's Where clause and came back as a 500.
        var response = await Client.GetAsync(LocalAuthoritiesRoute + "?onboardingStatus=bogus");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(OnboardingStatuses.Active)]
    [InlineData(OnboardingStatuses.Invited)]
    [InlineData(OnboardingStatuses.Suspended)]
    public async Task ShouldAcceptSupportedOnboardingStatusFilters(string status)
    {
        var response = await Client.GetAsync(LocalAuthoritiesRoute + "?onboardingStatus=" + status);

        response.IsSuccessStatusCode.Should().BeTrue();
    }

    [Fact]
    public async Task ShouldReject_WhenActivityStatusFilterIsNotSupported()
    {
        AuthenticateAs(Role.Lead, OrgLeadUserId, Org.Id.Value);

        var response = await Client.GetAsync("activities?status=bogus");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ShouldAcceptTheDraftPseudoStatusFilter()
    {
        // "draft" is not an ActivityStatus — the repository maps it onto IsComplete == false — so it has to
        // be allowed explicitly alongside the supported names.
        AuthenticateAs(Role.Lead, OrgLeadUserId, Org.Id.Value);

        var response = await Client.GetAsync("activities?status=" + ActivityStatuses.DraftFilter);

        response.IsSuccessStatusCode.Should().BeTrue();
    }

    // ---- AC5: unknown fields (mass-assignment protection) -------------------------------------------

    [Fact]
    public async Task ShouldReject_WhenTheBodyCarriesAnUndeclaredField()
    {
        // CreateLocalAuthorityRequestDto does not declare onboardingStatus; a new organisation is always
        // created "invited". The field used to be accepted and silently discarded.
        var json = LocalAuthorityJson(extra: Comma + Field("onboardingStatus", OnboardingStatuses.Active));

        var response = await PostJsonAsync(LocalAuthoritiesRoute, json);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertNoOrganisationCreatedAsync();
    }

    // ---- AC6: control characters, null bytes, CRLF, bidi overrides ----------------------------------

    [Theory]
    [InlineData(0x0000, "null byte")]
    [InlineData(0x0007, "C0 control")]
    [InlineData(0x007F, "delete")]
    [InlineData(0x0085, "C1 control")]
    [InlineData(0x202E, "right-to-left override, the Trojan Source character")]
    [InlineData(0x2066, "left-to-right isolate")]
    public async Task ShouldReject_WhenAStringCarriesAHostileCharacter(int codePoint, string description)
    {
        description.Should().NotBeEmpty();

        var json = LocalAuthorityJson(name: "A" + JsonEscape(codePoint) + "Council");

        var response = await PostJsonAsync(LocalAuthoritiesRoute, json);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertNoOrganisationCreatedAsync();
    }

    [Fact]
    public async Task ShouldNormaliseBenignInvisibleCharacters_RatherThanRejectThem()
    {
        // A zero-width space and a stray byte-order mark are what a paste out of Word or a web page leaves
        // behind. They are repaired quietly, because failing a long answer over a character its author cannot
        // see is not an actionable error.
        var name = "  Zero" + JsonEscape(0x200B) + "Width" + JsonEscape(0xFEFF) + " Council  ";

        var response = await PostJsonAsync(LocalAuthoritiesRoute, LocalAuthorityJson(name: name));

        response.IsSuccessStatusCode.Should().BeTrue();

        var dto = await response.ToResponseModel<LocalAuthorityDto>();
        var saved = await Context.Organisations.AsNoTracking().FirstAsync(x => x.Id == new OrganisationId(dto.Id));

        // Zero-width characters stripped, surrounding whitespace trimmed, the real text untouched.
        saved.Name.Should().Be("ZeroWidth Council");
    }

    [Fact]
    public async Task ShouldFoldCarriageReturnsToLineFeeds_AndKeepTabsAndNewlines()
    {
        var purpose = "first" + JsonEscape(CR) + JsonEscape(LF) + "second" + JsonEscape(TAB) + "indented";
        var json = OpenBrace + Field("name", "Windows line endings") + Comma + Field("purpose", purpose)
            + Comma + RawField("saveAsDraft", "true") + CloseBrace;

        var response = await PostJsonAsync(CollectionsRoute, json);

        response.IsSuccessStatusCode.Should().BeTrue();

        var saved = await Context.Collections.AsNoTracking().FirstAsync();

        // CRLF folded to a single LF; the tab and the surrounding prose survive untouched.
        saved.Purpose.Should().Be("first" + (char)LF + "second" + (char)TAB + "indented");
        saved.Purpose.Should().NotContain(((char)CR).ToString());
    }

    [Fact]
    public async Task ShouldPreserveNonBmpCharacters()
    {
        // A surrogate PAIR is a legitimate character. Only an unpaired half is rejected — a check that
        // refused every surrogate would refuse every emoji and every non-BMP script.
        var emoji = char.ConvertFromUtf32(0x1F4CA);
        var json = OpenBrace + Field("name", "Emoji " + emoji + " collection") + Comma
            + RawField("saveAsDraft", "true") + CloseBrace;

        var response = await PostJsonAsync(CollectionsRoute, json);

        response.IsSuccessStatusCode.Should().BeTrue();

        var saved = await Context.Collections.AsNoTracking().FirstAsync();
        saved.Name.Should().Be("Emoji " + emoji + " collection");
    }

    // ---- AC7: the same rules apply to a request that bypasses the client ----------------------------

    [Theory]
    [InlineData("")]
    [InlineData("Atlantis")]
    public async Task ShouldApplyTheSameRules_WhenTheRequestBypassesTheClient(string region)
    {
        // Posted as raw JSON, exactly as a script would, rather than through the typed DTO the form builds.
        // The rules fire identically because they live on the server, not in the form.
        var json = LocalAuthorityJson(region: region);

        var response = await PostJsonAsync(LocalAuthoritiesRoute, json);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertNoOrganisationCreatedAsync();
    }

    // ---- AC8: the security audit trail --------------------------------------------------------------

    [Fact]
    public async Task ShouldNotAuditAnOrdinaryFieldFailure()
    {
        // A typo is not a security event. Auditing every one would swamp an admin-facing table that has no
        // retention sweep, and hand any authenticated caller a say in how many transactions this service
        // commits.
        var request = ValidLocalAuthority() with { Name = string.Empty };

        var response = await Client.PostAsJsonAsync(LocalAuthoritiesRoute, request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await SecurityRejectionsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task ShouldAuditAControlCharacter_WithoutRecordingTheRejectedValue()
    {
        var json = LocalAuthorityJson(name: "Injected" + JsonEscape(0x0000) + "Name");

        var response = await PostJsonAsync(LocalAuthoritiesRoute, json);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var audited = await SecurityRejectionsAsync();
        audited.Should().ContainSingle();

        var entry = audited[0];
        entry.TargetType.Should().Be(AuditTargetType.Request);
        entry.After.Should().NotBeNull();
        entry.After.Should().Contain("Name");
        entry.After.Should().Contain("unsafe_character");

        // The field name is recorded; the value is not. The value is the attack payload, and whatever else
        // shared the field is the caller's personal data.
        entry.After.Should().NotContain("Injected");
    }

    [Fact]
    public async Task ShouldAuditAnUndeclaredField_AsAMassAssignmentAttempt()
    {
        var json = LocalAuthorityJson(extra: Comma + RawField("isSuperAdmin", "true"));

        var response = await PostJsonAsync(LocalAuthoritiesRoute, json);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var audited = await SecurityRejectionsAsync();
        audited.Should().ContainSingle();
        audited[0].After.Should().Contain("unknown_field");
    }

    // ---- Helpers ------------------------------------------------------------------------------------

    private const int CR = 0x000D;
    private const int LF = 0x000A;
    private const int TAB = 0x0009;

    private static readonly string Quote = ((char)0x22).ToString();
    private static readonly string OpenBrace = ((char)0x7B).ToString();
    private static readonly string CloseBrace = ((char)0x7D).ToString();
    private const string Comma = ",";

    /// <summary>
    /// A JSON string escape for a code point, built at run time. Writing these as literals in the source
    /// would embed invisible control characters in this file, which is exactly the hazard under test.
    /// </summary>
    private static string JsonEscape(int codePoint)
    {
        return ((char)0x5C) + "u" + codePoint.ToString("x4");
    }

    /// <summary>Wraps a value in JSON quotes, so a number can be sent as a string on purpose.</summary>
    private static string Quoted(string value)
    {
        return Quote + value + Quote;
    }

    private static string Field(string name, string value)
    {
        return Quote + name + Quote + ":" + Quote + value + Quote;
    }

    private static string RawField(string name, string value)
    {
        return Quote + name + Quote + ":" + value;
    }

    /// <summary>
    /// The valid create-local-authority body, with any one part swappable, so each case differs from a
    /// known-good request in exactly the way it is named for.
    /// </summary>
    private static string LocalAuthorityJson(
        string name = "Brand New Council",
        string region = "London",
        string allocatedAmount = "250000",
        string extra = "")
    {
        return OpenBrace
            + Field("name", name) + Comma
            + Field("onsCode", "E06000099") + Comma
            + Field("region", region) + Comma
            + Field("type", OrgTypes.LocalAuthority) + Comma
            + Field("eCode", "E1000099") + Comma
            + RawField("allocatedAmount", allocatedAmount)
            + extra
            + CloseBrace;
    }

    private static CreateLocalAuthorityRequestDto ValidLocalAuthority()
    {
        return new CreateLocalAuthorityRequestDto(
            Name: "Brand New Council",
            OnsCode: "E06000099",
            Region: "London",
            Type: OrgTypes.LocalAuthority,
            ECode: "E1000099",
            AllocatedAmount: 250000m);
    }

    private Task<HttpResponseMessage> PostJsonAsync(string route, string json)
    {
        return Client.PostAsync(route, new StringContent(json, Encoding.UTF8, "application/json"));
    }

    /// <summary>Only the seeded organisation should exist — the rejected request must not have created one.</summary>
    private async Task AssertNoOrganisationCreatedAsync()
    {
        (await Context.Organisations.AsNoTracking().CountAsync()).Should().Be(1);
    }

    private async Task<List<AuditLog>> SecurityRejectionsAsync()
    {
        return await Context.AuditLogs.AsNoTracking()
            .Where(x => x.Action == AuditAction.SecurityInputRejected)
            .ToListAsync();
    }

    private static async Task<(string? Error, List<EnvelopeItem> Errors)> ReadEnvelopeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        var errors = new List<EnvelopeItem>();
        if (root.TryGetProperty("errors", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            errors.AddRange(items.EnumerateArray().Select(item => new EnvelopeItem(
                item.GetProperty("property").GetString() ?? string.Empty,
                item.GetProperty("message").GetString() ?? string.Empty)));
        }

        return (root.TryGetProperty("error", out var error) ? error.GetString() : null, errors);
    }

    public async Task InitializeAsync()
    {
        AuthenticateAsAdmin();

        // Seeded before any AuthenticateAs against it: TestAuthHandler builds the organisationStatus claim by
        // looking the organisation up, so an unseeded id yields no claim at all.
        if (!await Context.Organisations.AsNoTracking().AnyAsync(x => x.Id == Org.Id))
        {
            await Context.Organisations.AddAsync(Org);
            await SaveChangesAsync();
        }
    }

    public async Task DisposeAsync()
    {
        Context.AuditLogs.RemoveRange(Context.AuditLogs);
        Context.Collections.RemoveRange(Context.Collections);
        Context.OrgFundingAllocations.RemoveRange(Context.OrgFundingAllocations);
        await SaveChangesAsync();

        Context.Organisations.RemoveRange(Context.Organisations);
        await SaveChangesAsync();

        AuthenticateAsAdmin();
    }

    private sealed record EnvelopeItem(string Property, string Message);
}