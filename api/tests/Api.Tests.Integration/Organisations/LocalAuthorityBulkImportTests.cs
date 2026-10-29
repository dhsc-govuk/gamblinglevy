using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Api.Dtos;
using Application.Common.Settings;
using Domain.AuditLogs;
using Domain.Organisations;
using Domain.ValueObjects;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tests.Common;
using Tests.Common.Services;
using Tests.Data;
using Xunit;

namespace Api.Tests.Integration.Organisations;

public class LocalAuthorityBulkImportTests(IntegrationTestWebFactory factory)
    : BaseIntegrationTest(factory), IAsyncLifetime
{
    private const string ValidHeader = "name,ons_code,region,type,e_code,allocated_amount\n";

    private const string HeaderWithAdmin =
        "name,ons_code,region,type,e_code,allocated_amount,la_admin_first_name,la_admin_last_name,la_admin_access_level,la_admin_email\n";

    // ApplicationSettings is a singleton, so this is the live instance the handler reads. Retuning
    // BulkImport for one test is safe in a way retuning, say, RateLimiting would not be: no other test class
    // touches the importer. InitializeAsync puts both values back.
    private readonly ApplicationSettings _settings = factory.Services.GetRequiredService<ApplicationSettings>();

    [Fact]
    public async Task ShouldBulkImport_NewAndExistingRows_ReportsOutcomes()
    {
        var existing = Organisation.New(
            OrganisationId.New(),
            "Existing Council",
            "E06000010",
            Region.London,
            OrgType.LocalAuthority,
            "E1000001",
            OnboardingStatus.Invited,
            DateTime.UtcNow);
        await Context.Organisations.AddAsync(existing);
        await SaveChangesAsync();

        var csv =
            ValidHeader +
            "Existing Council Renamed,E06000010,London,LA,E-1,500000\n" +
            "Brand New Council,E06000011,North West,LA,E-2,250000\n" +
            "Invalid Row,E06000012,Not A Region,LA,E-3,100\n";

        var result = await ImportCsvAsync(csv);

        result.CreatedCount.Should().Be(1);
        result.UpdatedCount.Should().Be(1);
        result.FailedCount.Should().Be(1);
        result.Rows.Should().HaveCount(3);

        var updated = await Context.Organisations.AsNoTracking().FirstAsync(x => x.OnsCode == "E06000010");
        updated.Name.Should().Be("Existing Council Renamed");

        // First-time allocation creation — AmendedAt only set on subsequent updates (see ShouldReuseFundingWindow_OnRepeatedImport).
        var updatedAllocation = await Context.OrgFundingAllocations.AsNoTracking().FirstAsync(x => x.OrganisationId == updated.Id);
        updatedAllocation.AllocatedAmount.Should().Be(500000m);
    }

    [Fact]
    public async Task ShouldSetAmendedAt_OnRepeatImportOfSameOrg()
    {
        var csvFirst = ValidHeader + "Council B,E06000040,London,LA,E-1,100\n";
        var csvSecond = ValidHeader + "Council B,E06000040,London,LA,E-1,200\n";

        await ImportCsvAsync(csvFirst);
        await ImportCsvAsync(csvSecond);

        var org = await Context.Organisations.AsNoTracking().FirstAsync(x => x.OnsCode == "E06000040");
        var allocation = await Context.OrgFundingAllocations.AsNoTracking().FirstAsync(x => x.OrganisationId == org.Id);
        allocation.AllocatedAmount.Should().Be(200m);
        allocation.AmendedAt.Should().NotBeNull();
        allocation.AmendedBy.Should().NotBeNull();
    }

    [Fact]
    public async Task ShouldRejectRow_WhenQuarterlyColumnPresent()
    {
        var csv =
            "name,ons_code,region,type,e_code,allocated_amount,q1_expected_spend\n" +
            "Has Quarterly,E06000020,London,LA,E-1,500000,100\n";

        var result = await ImportCsvAsync(csv);
        result.FailedCount.Should().Be(1);
        result.Rows[0].Reason.Should().Contain("Quarterly profile columns");
    }

    // BLUM-7051: ons_code is mandatory for a Local Authority row, so a blank cell fails that row alone —
    // the rest of the file still imports.
    [Fact]
    public async Task ShouldFailRow_WhenOnsCodeIsBlank_ForALocalAuthority()
    {
        var csv =
            ValidHeader +
            "No Code Council,,London,LA,E-1,100000\n" +
            "Coded Council,E06000013,London,LA,E-2,200000\n";

        var result = await ImportCsvAsync(csv);

        result.CreatedCount.Should().Be(1);
        result.FailedCount.Should().Be(1);
        result.Rows[0].Reason.Should().Contain("ons_code is required for Local Authority rows");
        (await Context.Organisations.AsNoTracking().AnyAsync(x => x.Name == "No Code Council")).Should().BeFalse();
        (await Context.Organisations.AsNoTracking().AnyAsync(x => x.Name == "Coded Council")).Should().BeTrue();
    }

    // A VCSE row has no ONS code by nature, so the same blank cell must import cleanly. Without the type
    // condition this row would fail and no VCSE organisation could be bulk imported at all.
    [Fact]
    public async Task ShouldCreateVcseRow_WhenOnsCodeIsBlank()
    {
        var csv =
            ValidHeader +
            "Support Charity,,London,VCSE,E-1,100000\n";

        var result = await ImportCsvAsync(csv);

        result.CreatedCount.Should().Be(1);
        result.FailedCount.Should().Be(0);
        var created = await Context.Organisations.AsNoTracking().FirstAsync(x => x.Name == "Support Charity");
        created.OnsCode.Should().BeNull();
        created.Type.ToString().Should().Be("VCSE");
    }

    // A missing *column* is a property of the header, not of any one row, so it is rejected once for the
    // whole file. Checking it per row turned one mistake into one identical failure per data row.
    [Fact]
    public async Task ShouldRejectFile_WhenARequiredColumnIsAbsent_WithoutFanningOutPerRow()
    {
        var csv =
            "name,ons_code,type,e_code,allocated_amount\n" +
            "Council One,E06000201,LA,E-1,100000\n" +
            "Council Two,E06000202,LA,E-2,100000\n" +
            "Council Three,E06000203,LA,E-3,100000\n";

        var response = await PostCsvAsync(csv);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var (_, message) = await ReadErrorAsync(response);
        message.Should().Contain("region");
        (await Context.Organisations.AsNoTracking().AnyAsync()).Should().BeFalse();
    }

    // Dropping ons_code entirely is legal — VCSE rows do not need it — so the file parses and only the LA
    // rows fail, each naming the column it needs.
    [Fact]
    public async Task ShouldFailOnlyLaRows_WhenOnsCodeColumnIsAbsent()
    {
        var csv =
            "name,region,type,e_code,allocated_amount\n" +
            "Minimal Council,London,LA,E-1,100000\n" +
            "Minimal Charity,London,VCSE,E-2,100000\n";

        var result = await ImportCsvAsync(csv);

        result.CreatedCount.Should().Be(1);
        result.FailedCount.Should().Be(1);
        result.Rows[0].Reason.Should().Contain("ons_code");
        (await Context.Organisations.AsNoTracking().AnyAsync(x => x.Name == "Minimal Council")).Should().BeFalse();
        (await Context.Organisations.AsNoTracking().AnyAsync(x => x.Name == "Minimal Charity")).Should().BeTrue();
    }

    // BLUM-7050: e_code is required for every organisation type, so unlike ons_code it can be a blanket column
    // rule — a missing column is reported once against the header rather than once per data row.
    [Fact]
    public async Task ShouldRejectFile_WhenECodeColumnIsAbsent_WithoutFanningOutPerRow()
    {
        var csv =
            "name,ons_code,region,type,allocated_amount\n" +
            "Council One,E06000231,London,LA,100000\n" +
            "Council Two,E06000232,London,LA,100000\n" +
            "Council Three,E06000233,London,LA,100000\n";

        var response = await PostCsvAsync(csv);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var (_, message) = await ReadErrorAsync(response);
        message.Should().Contain("e_code");
        (await Context.Organisations.AsNoTracking().AnyAsync()).Should().BeFalse();
    }

    // The other half of the split: the column is present but a cell is blank, which is row-specific
    // information, so that row alone fails and the rest of the file still imports.
    [Fact]
    public async Task ShouldFailRow_WhenECodeIsBlank()
    {
        var csv =
            ValidHeader +
            "No E-code Council,E06000240,London,LA,,100000\n" +
            "E-coded Council,E06000241,London,LA,E-1,200000\n";

        var result = await ImportCsvAsync(csv);

        result.CreatedCount.Should().Be(1);
        result.FailedCount.Should().Be(1);
        result.Rows[0].Reason.Should().Contain("e_code");
        (await Context.Organisations.AsNoTracking().AnyAsync(x => x.Name == "No E-code Council")).Should().BeFalse();
        (await Context.Organisations.AsNoTracking().AnyAsync(x => x.Name == "E-coded Council")).Should().BeTrue();
    }

    // BLUM-7050 review: the template endpoint and the importer's RequiredColumns used to be two hand-maintained
    // literals, so a column change on one side alone would hand admins an official template the importer rejects.
    // This drives the real round trip — download the template, fill in one row, upload it — which is the only
    // thing that proves the two agree regardless of how they are implemented.
    [Fact]
    public async Task ShouldImport_AFileBuiltFromTheDownloadedTemplate()
    {
        var templateResponse = await Client.GetAsync("organisations/local-authorities/bulk-import/template");
        templateResponse.IsSuccessStatusCode.Should().BeTrue();
        var header = await templateResponse.Content.ReadAsStringAsync();

        // One value per column of the header, in its order — exactly what an admin filling in the template does.
        var columns = header.TrimEnd('\n').Split(',');
        var values = columns.Select(c => c switch
        {
            "name" => "Template Council",
            "ons_code" => "E06000270",
            "region" => "London",
            "type" => "LA",
            "e_code" => "E-1",
            "allocated_amount" => "100000",
            "la_admin_first_name" => "Template",
            "la_admin_last_name" => "Admin",
            "la_admin_access_level" => "LA Admin",
            "la_admin_email" => "template.admin@example.gov.uk",
            _ => throw new InvalidOperationException($"Template column '{c}' has no test value — add one."),
        });

        var result = await ImportCsvAsync(header + string.Join(',', values) + "\n");

        result.CreatedCount.Should().Be(1);
        result.FailedCount.Should().Be(0);
        result.AdminUsersCreatedCount.Should().Be(1);
        (await Context.Organisations.AsNoTracking().AnyAsync(x => x.Name == "Template Council")).Should().BeTrue();
        (await Context.Users.AsNoTracking().AnyAsync(x => x.Email == "template.admin@example.gov.uk")).Should().BeTrue();
    }

    // BLUM-7050 review: an over-long e_code used to reach the varchar(64) column, and the failed SaveChanges left
    // the entity tracked in Added state on the scoped DbContext — so every *subsequent* row's SaveChanges retried
    // that same failing insert and the whole rest of the file failed with a raw PostgreSQL error. The row-level
    // length check must reject only its own row and leave the others importable.
    [Fact]
    public async Task ShouldFailOnlyItsOwnRow_WhenECodeExceedsTheLengthLimit()
    {
        var csv =
            ValidHeader +
            $"Too Long Council,E06000250,London,LA,{new string('E', 65)},100000\n" +
            "Fine Council One,E06000251,London,LA,E-1,200000\n" +
            "Fine Council Two,E06000252,London,LA,E-2,300000\n";

        var result = await ImportCsvAsync(csv);

        result.FailedCount.Should().Be(1);
        result.CreatedCount.Should().Be(2);
        result.Rows[0].Reason.Should().Contain("e_code must be 64 characters or fewer");
        // The rows after the bad one must be unaffected — this is the regression the fix exists for.
        result.Rows[1].Reason.Should().BeNull();
        result.Rows[2].Reason.Should().BeNull();
        (await Context.Organisations.AsNoTracking().AnyAsync(x => x.Name == "Too Long Council")).Should().BeFalse();
        (await Context.Organisations.AsNoTracking().AnyAsync(x => x.Name == "Fine Council One")).Should().BeTrue();
        (await Context.Organisations.AsNoTracking().AnyAsync(x => x.Name == "Fine Council Two")).Should().BeTrue();
    }

    // BLUM-7240 — the same defect class as the e_code case above, for the three other cells that reach a
    // bounded column: name is varchar(255), ons_code varchar(64) and allocated_amount numeric(18,2). Each was
    // caught only by the generic catch at the end of the row loop, which is the poisoned-context path the
    // comment above describes, so one oversized cell failed the rest of the file too.
    [Theory]
    [InlineData("name", "name must be 255 characters or fewer")]
    [InlineData("ons_code", "ons_code must be 64 characters or fewer")]
    [InlineData("allocated_amount", "allocated_amount must be")]
    public async Task ShouldFailOnlyItsOwnRow_WhenACellExceedsItsColumn(string column, string expectedReason)
    {
        var name = column == "name" ? new string('N', 256) : "Oversized Council";
        var onsCode = column == "ons_code" ? new string('E', 65) : "E06000270";
        var amount = column == "allocated_amount" ? "10000000000000" : "100000";

        var csv =
            ValidHeader +
            $"{name},{onsCode},London,LA,E-9,{amount}\n" +
            "Unaffected Council,E06000271,London,LA,E-8,200000\n";

        var result = await ImportCsvAsync(csv);

        result.FailedCount.Should().Be(1);
        result.CreatedCount.Should().Be(1);
        result.Rows[0].Reason.Should().Contain(expectedReason);

        // The row after the bad one must still import — that is the regression, not the rejection itself.
        result.Rows[1].Reason.Should().BeNull();
        (await Context.Organisations.AsNoTracking().AnyAsync(x => x.Name == "Unaffected Council")).Should().BeTrue();
    }

    // BLUM-7240 / CWE-20 — the CSV route is the API's other ingestion path, and SanitisedStringJsonConverter
    // never sees it: that converter is registered on AddJsonOptions, so a multipart upload bypassed it
    // entirely and a bidi override or a null byte reached organisations.name unexamined. From there it
    // renders reversed in the admin list and is written into the quarterly-report ZIP a DHSC analyst opens.
    //
    // A hostile character fails the whole upload rather than the row: a council's spreadsheet does not
    // contain one by accident, so this is not a file to import three-quarters of.
    [Theory]
    [InlineData(0x0000, "null byte")]
    [InlineData(0x001B, "escape")]
    [InlineData(0x202E, "right-to-left override, the Trojan Source character")]
    public async Task ShouldRejectTheWholeUpload_WhenACellCarriesAHostileCharacter(int codePoint, string why)
    {
        why.Should().NotBeEmpty();

        var csv =
            ValidHeader +
            $"Injected{(char)codePoint}Council,E06000280,London,LA,E-7,100000\n" +
            "Innocent Council,E06000281,London,LA,E-6,200000\n";

        var response = await PostCsvAsync(csv);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Context.Organisations.AsNoTracking().AnyAsync()).Should().BeFalse();
    }

    // The benign half of the same policy, and the reason it is not simply "reject anything unusual": a
    // spreadsheet pasted together from Word and a web page routinely carries zero-width characters and a
    // byte-order mark, and failing a 150-row import over one is not an actionable error.
    [Fact]
    public async Task ShouldStripInvisibleCharactersFromCells_AndStillImport()
    {
        var name = $"Zero{(char)0x200B}Width{(char)0xFEFF} Council";
        var csv = ValidHeader + $"{name},E06000282,London,LA,E-5,100000\n";

        var result = await ImportCsvAsync(csv);

        result.CreatedCount.Should().Be(1);
        (await Context.Organisations.AsNoTracking().AnyAsync(x => x.Name == "ZeroWidth Council")).Should().BeTrue();
    }

    // Exactly at the limit is valid, and surrounding whitespace is trimmed before the length is measured.
    [Fact]
    public async Task ShouldAcceptAnECode_AtExactlyTheLengthLimit()
    {
        var maxLength = new string('E', 64);
        var csv = ValidHeader + $"Max Length Council,E06000260,London,LA,  {maxLength}  ,100000\n";

        var result = await ImportCsvAsync(csv);

        result.CreatedCount.Should().Be(1);
        result.FailedCount.Should().Be(0);
        var saved = await Context.Organisations.AsNoTracking().FirstAsync(x => x.Name == "Max Length Council");
        saved.ECode.Should().Be(maxLength);
    }

    // Finding: an ONS-code match used to rename the matched organisation with no duplicate-name check, so a
    // row pairing one organisation's name with another's code silently produced two identically named rows
    // that the edit form then refused to save.
    [Fact]
    public async Task ShouldFailRow_WhenAnOnsMatchWouldRenameOntoAnExistingName()
    {
        await ImportCsvAsync(ValidHeader + "Camden,E09000007,London,LA,E-1,100000\n" + "Barnet,E09000003,London,LA,E-1,100000\n");

        var result = await ImportCsvAsync(ValidHeader + "Barnet,E09000007,London,LA,E-1,100000\n");

        result.FailedCount.Should().Be(1);
        result.UpdatedCount.Should().Be(0);
        result.Rows[0].Reason.Should().Contain("already exists");
        (await Context.Organisations.AsNoTracking().CountAsync(x => x.Name == "Barnet")).Should().Be(1);
        (await Context.Organisations.AsNoTracking().FirstAsync(x => x.OnsCode == "E09000007")).Name.Should().Be("Camden");
    }

    // The ONS code is the only match key, and it is free text on the edit form, so a casing difference must
    // still resolve to the same organisation rather than falling through to a rejected create.
    [Fact]
    public async Task ShouldUpdateByOnsCode_WhenCasingDiffers()
    {
        await ImportCsvAsync(ValidHeader + "Case Council,e06000210,London,LA,E-1,100000\n");

        var result = await ImportCsvAsync(ValidHeader + "Case Council Renamed,E06000210,London,LA,E-1,200000\n");

        result.UpdatedCount.Should().Be(1);
        result.CreatedCount.Should().Be(0);
        (await Context.Organisations.AsNoTracking().CountAsync(x => x.Name.StartsWith("Case Council"))).Should().Be(1);
    }

    // With ons_code required, a duplicate name can only arrive under a *different* code — i.e. an admin
    // trying to add a second organisation with a name already in use rather than updating the original.
    // That must still fail loudly instead of creating a twin.
    [Fact]
    public async Task ShouldFailRow_WhenNameAlreadyExists_UnderADifferentOnsCode()
    {
        (await ImportCsvAsync(ValidHeader + "Repeat Council,E06000120,London,LA,E-1,100000\n"))
            .CreatedCount.Should().Be(1);

        var second = await ImportCsvAsync(ValidHeader + "Repeat Council,E06000121,London,LA,E-1,100000\n");

        second.CreatedCount.Should().Be(0);
        second.FailedCount.Should().Be(1);
        second.Rows[0].Reason.Should().Contain("already exists");
        (await Context.Organisations.AsNoTracking().CountAsync(x => x.Name == "Repeat Council")).Should().Be(1);
    }

    // An ONS code is still required to *update*: it is the only match key.
    [Fact]
    public async Task ShouldStillUpdateByOnsCode_WhenOnsCodeIsPresent()
    {
        var csv = ValidHeader + "Keyed Council,E06000110,London,LA,E-1,100000\n";
        await ImportCsvAsync(csv);

        var renamed = await ImportCsvAsync(ValidHeader + "Keyed Council Renamed,E06000110,London,LA,E-1,200000\n");

        renamed.UpdatedCount.Should().Be(1);
        renamed.CreatedCount.Should().Be(0);
        (await Context.Organisations.AsNoTracking().CountAsync(x => x.OnsCode == "E06000110")).Should().Be(1);
    }

    // The create form labels the "LA" value "Local Authority", so that is what an admin writes in a CSV.
    // Values are matched case-insensitively and stored canonically.
    [Theory]
    [InlineData("Local authority", "LA")]
    [InlineData("Local Authority", "LA")]
    [InlineData("LA", "LA")]
    [InlineData("la", "LA")]
    [InlineData("vcse", "VCSE")]
    public async Task ShouldResolveTypeValue_CaseInsensitively_AndByDisplayLabel(string input, string expected)
    {
        var csv =
            ValidHeader +
            $"Type Council,E06000120,London,{input},E-1,1000\n";

        var result = await ImportCsvAsync(csv);

        result.CreatedCount.Should().Be(1);
        result.FailedCount.Should().Be(0);
        var org = await Context.Organisations.AsNoTracking().FirstAsync(x => x.OnsCode == "E06000120");
        org.Type.ToString().Should().Be(expected);
    }

    [Theory]
    [InlineData("london", "London")]
    [InlineData("NORTH WEST", "North West")]
    [InlineData("yorkshire and the humber", "Yorkshire and the Humber")]
    public async Task ShouldResolveRegionValue_CaseInsensitively_AndStoreCanonically(string input, string expected)
    {
        var csv =
            ValidHeader +
            $"Region Council,E06000130,{input},LA,E-1,1000\n";

        var result = await ImportCsvAsync(csv);

        result.CreatedCount.Should().Be(1);
        var org = await Context.Organisations.AsNoTracking().FirstAsync(x => x.OnsCode == "E06000130");
        // Canonical, not the spelling supplied — the column is written from Region.ToString().
        org.Region.ToString().Should().Be(expected);
    }

    // A genuinely unknown value must still fail, and the reason must list what is accepted rather than
    // echoing the raw domain exception ("OrgType \"X\" is not supported.").
    [Fact]
    public async Task ShouldFailRow_WithAcceptedValues_WhenTypeIsUnknown()
    {
        var csv = ValidHeader + "Bad Type Council,E06000140,London,Charity,E-1,1000\n";

        var result = await ImportCsvAsync(csv);

        result.FailedCount.Should().Be(1);
        result.Rows[0].Reason.Should().Contain("Charity");
        result.Rows[0].Reason.Should().Contain("VCSE");
        result.Rows[0].Reason.Should().NotContain("is not supported");
    }

    [Fact]
    public async Task ShouldFailRow_WithAcceptedValues_WhenRegionIsUnknown()
    {
        var csv = ValidHeader + "Bad Region Council,E06000150,Atlantis,LA,E-1,1000\n";

        var result = await ImportCsvAsync(csv);

        result.FailedCount.Should().Be(1);
        result.Rows[0].Reason.Should().Contain("Atlantis");
        result.Rows[0].Reason.Should().Contain("Yorkshire and the Humber");
    }

    [Fact]
    public async Task ShouldReuseFundingWindow_OnRepeatedImport()
    {
        var csv = ValidHeader + "Council A,E06000030,London,LA,E-1,100\n";

        await ImportCsvAsync(csv);
        await ImportCsvAsync(csv);

        (await Context.FundingWindows.AsNoTracking().CountAsync()).Should().Be(1);
    }

    // The reported bug (BLUM-7024): the downloaded template is header-only, so uploading it unmodified
    // used to parse into zero records and return 200 with all-zero counts and no error at all.
    [Fact]
    public async Task ShouldReturnBadRequest_WhenCsvHasHeaderOnly()
    {
        var response = await PostCsvAsync(ValidHeader);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var (error, message) = await ReadErrorAsync(response);
        error.Should().Be("BULK_IMPORT_NO_DATA_ROWS");
        message.Should().Contain("no Local Authority rows");

        (await Context.Organisations.AsNoTracking().CountAsync()).Should().Be(0);
        // Guards the ordering: ResolveCurrentAsync persists a window, so a guard placed below it would
        // leave this row behind even though the request was rejected.
        (await Context.FundingWindows.AsNoTracking().CountAsync()).Should().Be(0);
        (await Context.AuditLogs.AsNoTracking().CountAsync(x => x.Action == AuditAction.OrganisationBulkImported)).Should().Be(0);
    }

    [Fact]
    public async Task ShouldReturnBadRequest_WhenCsvHasOnlyBlankLines()
    {
        var response = await PostCsvAsync(ValidHeader + "\n\n\n");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var (error, _) = await ReadErrorAsync(response);
        error.Should().Be("BULK_IMPORT_NO_DATA_ROWS");
    }

    [Fact]
    public async Task ShouldReturnBadRequest_WithSpecificMessage_WhenFileIsNotCsv()
    {
        var response = await PostCsvAsync(ValidHeader + "Council C,E06000050,London,LA,E-1,100\n", "upload.txt");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var (error, message) = await ReadErrorAsync(response);
        error.Should().Be("BULK_IMPORT_FILE_NOT_CSV");
        message.Should().Contain("must be a CSV file");
        // The defect this replaces: the real reason was wrapped in OrganisationUnknownException and the
        // user was shown "Unknown exception for the organisation OrganisationId { Value = 000... }" as a 500.
        message.Should().NotContain("Unknown exception");
    }

    [Fact]
    public async Task ShouldReturnBadRequest_WhenFileIsEmpty()
    {
        var response = await PostCsvAsync(string.Empty);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var (error, _) = await ReadErrorAsync(response);
        error.Should().Be("BULK_IMPORT_FILE_EMPTY");
    }

    [Fact]
    public async Task ShouldReturnBadRequest_WhenFileHasNoHeaderRow()
    {
        var response = await PostCsvAsync("\n\n\n");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var (error, message) = await ReadErrorAsync(response);
        error.Should().Be("BULK_IMPORT_FILE_INVALID");
        message.Should().Contain("header row");
        // The parser's own developer-facing exception text must never reach the error summary.
        message.Should().NotContain("Error parsing CSV file");
    }

    // Case-normalising headers can map two columns onto one key; assigning by indexer would let the blank
    // trailing column silently overwrite the populated one rather than fail.
    [Fact]
    public async Task ShouldReturnBadRequest_WhenCsvHasCaseVariantDuplicateColumns()
    {
        var csv =
            "name,ons_code,region,type,e_code,allocated_amount,Region\n" +
            "Dup Council,E06000090,London,LA,E-1,1000,\n";

        var response = await PostCsvAsync(csv);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var (error, message) = await ReadErrorAsync(response);
        error.Should().Be("BULK_IMPORT_FILE_INVALID");
        message.Should().Contain("duplicate columns");
        message.Should().Contain("region");
        (await Context.Organisations.AsNoTracking().CountAsync()).Should().Be(0);
    }

    // A 0-byte .xlsx is a wrong-format problem; reporting it as empty sends the user off to add rows and
    // re-upload the same unusable format.
    [Fact]
    public async Task ShouldReportWrongFormat_NotEmptiness_WhenZeroByteFileIsNotCsv()
    {
        var response = await PostCsvAsync(string.Empty, "upload.xlsx");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var (error, _) = await ReadErrorAsync(response);
        error.Should().Be("BULK_IMPORT_FILE_NOT_CSV");
    }

    // Column presence must come from the header row, not from whether a given row happened to carry a
    // trailing comma — otherwise a stray forbidden column rejects some rows and imports others.
    [Fact]
    public async Task ShouldRejectEveryRow_WhenQuarterlyColumnPresent_RegardlessOfTrailingCommas()
    {
        var csv =
            "name,ons_code,region,type,e_code,allocated_amount,Q1_Expected_Spend\n" +
            "With Trailing Comma,E06000100,London,LA,E-1,500000,\n" +
            "Without Trailing Comma,E06000101,London,LA,E-2,600000\n";

        var result = await ImportCsvAsync(csv);

        result.FailedCount.Should().Be(2);
        result.CreatedCount.Should().Be(0);
        result.Rows.Should().OnlyContain(r => r.Reason!.Contains("Quarterly profile columns"));
        (await Context.Organisations.AsNoTracking().CountAsync()).Should().Be(0);
    }

    // The split: a missing *column* is a property of the header and is rejected once for the whole file,
    // because its per-row reason is identical on every row and carries no row-specific information. Missing
    // or invalid *values* stay a 200 with per-row reasons, which do tell the user which row and why.
    [Fact]
    public async Task ShouldReturnBadRequest_WhenTheHeaderHasNoneOfTheRequiredColumns()
    {
        var response = await PostCsvAsync("foo,bar\n1,2\n");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var (_, message) = await ReadErrorAsync(response);
        message.Should().Contain("Missing required columns");
    }

    // The other half of that split — every row parses, every row fails, and the response is still a 200
    // carrying one distinct reason per row.
    [Fact]
    public async Task ShouldReturnOk_WithRowFailures_WhenAllRowsHaveInvalidValues()
    {
        var result = await ImportCsvAsync(
            ValidHeader +
            "Bad Region Co,E06000220,Atlantis,LA,E-1,100\n" +
            "Bad Type Co,E06000221,London,Charity,E-1,100\n");

        result.FailedCount.Should().Be(2);
        result.CreatedCount.Should().Be(0);
        result.Rows[0].Reason.Should().Contain("region");
        result.Rows[1].Reason.Should().Contain("type");
    }

    [Fact]
    public async Task ShouldImport_WhenHeadersAreMixedCase()
    {
        var csv =
            "Name,ONS_Code,Region,Type,E_Code,Allocated_Amount\n" +
            "Mixed Case Council,E06000060,London,LA,E-9,1000\n";

        var result = await ImportCsvAsync(csv);

        result.CreatedCount.Should().Be(1);
        result.FailedCount.Should().Be(0);
        (await Context.Organisations.AsNoTracking().AnyAsync(x => x.OnsCode == "E06000060")).Should().BeTrue();
    }

    [Fact]
    public async Task ShouldRejectRow_WhenQuarterlyColumnPresent_MixedCase()
    {
        var csv =
            "name,ons_code,region,type,e_code,allocated_amount,Q1_Expected_Spend\n" +
            "Mixed Quarterly,E06000070,London,LA,E-1,500000,100\n";

        var result = await ImportCsvAsync(csv);

        result.FailedCount.Should().Be(1);
        result.Rows[0].Reason.Should().Contain("Quarterly profile columns");
    }

    [Fact]
    public async Task ShouldForbidNonAdmin_FromBulkImporting()
    {
        AuthenticateAs(Role.OrganisationAdmin, Guid.NewGuid(), Guid.NewGuid());

        var response = await PostCsvAsync(ValidHeader + "Council D,E06000080,London,LA,E-1,100\n");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // BLUM-7609 AC1/AC2: the four la_admin_* columns create the organisation's first user in the same pass.
    [Fact]
    public async Task ShouldCreateLaAdminUser_WhenAdminColumnsArePresent()
    {
        var csv = HeaderWithAdmin + "Admin Council,E06000200,London,LA,E-1,100000,Jane,Smith,LA Admin,jane.smith@admincouncil.example\n";

        var result = await ImportCsvAsync(csv);

        // Read first, before the database assertions below. InMemoryInvitationEmailService.Sent is a static bag
        // shared by the whole assembly and AdminUsersControllerTests clears it in its own InitializeAsync, so
        // with xUnit.MaxParallelThreads=2 anything between the import and this read is a window in which that
        // Clear() can land and take this entry with it.
        var invited = InMemoryInvitationEmailService.Sent.ToList();

        result.CreatedCount.Should().Be(1);
        result.AdminUsersCreatedCount.Should().Be(1);
        result.Rows.Single().AdminUserOutcome.Should().Be("Created");
        result.Rows.Single().AdminUserEmail.Should().Be("jane.smith@admincouncil.example");
        result.Rows.Single().AdminUserReason.Should().BeNull();

        var org = await Context.Organisations.AsNoTracking().FirstAsync(x => x.OnsCode == "E06000200");
        var organisationUser = await Context.OrganisationUsers.AsNoTracking()
            .Include(x => x.User)
            .FirstAsync(x => x.OrganisationId == org.Id);

        organisationUser.User!.Email.Should().Be("jane.smith@admincouncil.example");
        organisationUser.User.Name.FirstName.Should().Be("Jane");
        organisationUser.User.Name.LastName.Should().Be("Smith");
        organisationUser.User.Role.Should().Be(UserRole.OrganisationAdmin);

        // Deferring activation would email someone an invitation to an account that refuses them.
        organisationUser.User.Status.Should().Be(UserStatus.Active);

        invited.Should().Contain(s => s.Email == "jane.smith@admincouncil.example");
    }

    [Fact]
    public async Task ShouldImportOrganisation_WithoutAnAdminUser_WhenAdminColumnsAreBlank()
    {
        var csv = HeaderWithAdmin + "Blank Admin Council,E06000201,London,LA,E-1,100000,,,,\n";

        var result = await ImportCsvAsync(csv);

        result.CreatedCount.Should().Be(1);
        result.FailedCount.Should().Be(0);
        result.AdminUsersCreatedCount.Should().Be(0);
        result.Rows.Single().AdminUserOutcome.Should().Be("NotProvided");
        result.Rows.Single().AdminUserReason.Should().BeNull();
        (await HasAnyUserAsync("E06000201")).Should().BeFalse();
    }

    // BLUM-7609 AC3: a file written against the pre-BLUM-7609 template has none of the new columns at all and
    // must still import exactly as before. This is why the la_admin_* columns are in OptionalColumns rather
    // than falling into the derived RequiredColumns set.
    [Fact]
    public async Task ShouldStillImport_WhenTheFileHasNoAdminColumnsAtAll()
    {
        var result = await ImportCsvAsync(ValidHeader + "Legacy Template Council,E06000202,London,LA,E-1,100000\n");

        result.CreatedCount.Should().Be(1);
        result.FailedCount.Should().Be(0);
        result.AdminUsersCreatedCount.Should().Be(0);
        result.Rows.Single().AdminUserOutcome.Should().Be("NotProvided");
        (await HasAnyUserAsync("E06000202")).Should().BeFalse();
    }

    // A CSV is the only free-text way into the access level, so the labels the UI renders ("LA Admin",
    // "LA Lead") have to resolve, as well as the wire values themselves.
    [Theory]
    [InlineData("LA Admin", Role.OrganisationAdmin)]
    [InlineData("la admin", Role.OrganisationAdmin)]
    [InlineData("organisation admin", Role.OrganisationAdmin)]
    [InlineData("LA Lead", Role.Lead)]
    [InlineData("lead", Role.Lead)]
    [InlineData("LEAD", Role.Lead)]
    public async Task ShouldResolveAccessLevel_ByLabelAndRoleString_CaseInsensitively(string input, string expectedRole)
    {
        var csv = HeaderWithAdmin + $"Access Level Council,E06000203,London,LA,E-1,100000,Alex,Doe,{input},alex.doe@accesslevel.example\n";

        var result = await ImportCsvAsync(csv);

        result.AdminUsersCreatedCount.Should().Be(1);

        var user = await Context.Users.AsNoTracking().FirstAsync(x => x.Email == "alex.doe@accesslevel.example");
        user.Role.Should().Be(UserRole.From(expectedRole));
    }

    [Fact]
    public async Task ShouldFailOnlyTheAdminUser_WhenAccessLevelIsUnrecognised()
    {
        var csv = HeaderWithAdmin + "Bad Level Council,E06000204,London,LA,E-1,100000,Alex,Doe,Manager,alex.doe@badlevel.example\n";

        var result = await ImportCsvAsync(csv);

        // The organisation really is in the database, so the row must not claim otherwise.
        result.CreatedCount.Should().Be(1);
        result.FailedCount.Should().Be(0);
        result.AdminUsersCreatedCount.Should().Be(0);

        var row = result.Rows.Single();
        row.Outcome.Should().Be("Created");
        row.AdminUserOutcome.Should().Be("Failed");
        row.AdminUserReason.Should().Contain("Manager").And.Contain("LA Admin").And.Contain("LA Lead");

        (await Context.Organisations.AsNoTracking().AnyAsync(x => x.OnsCode == "E06000204")).Should().BeTrue();
        (await HasAnyUserAsync("E06000204")).Should().BeFalse();
    }

    [Fact]
    public async Task ShouldFailOnlyTheAdminUser_WhenEmailIsMalformed()
    {
        var csv = HeaderWithAdmin + "Bad Email Council,E06000205,London,LA,E-1,100000,Alex,Doe,LA Admin,not-an-email\n";

        var result = await ImportCsvAsync(csv);

        result.CreatedCount.Should().Be(1);
        var row = result.Rows.Single();
        row.AdminUserOutcome.Should().Be("Failed");
        row.AdminUserReason.Should().Contain("la_admin_email").And.Contain("not a valid email address");
        (await HasAnyUserAsync("E06000205")).Should().BeFalse();
    }

    [Fact]
    public async Task ShouldFailOnlyTheAdminUser_WhenOnlySomeAdminColumnsAreFilled()
    {
        var csv = HeaderWithAdmin + "Partial Admin Council,E06000206,London,LA,E-1,100000,Alex,,LA Admin,alex.doe@partial.example\n";

        var result = await ImportCsvAsync(csv);

        result.CreatedCount.Should().Be(1);
        var row = result.Rows.Single();
        row.AdminUserOutcome.Should().Be("Failed");
        row.AdminUserReason.Should().Contain("must all be filled in");
        (await HasAnyUserAsync("E06000206")).Should().BeFalse();
    }

    // Re-uploading a corrected file is the normal way to fix one row, so the second pass must update the
    // organisation and leave the person alone rather than failing or inviting them twice.
    [Fact]
    public async Task ShouldReportAlreadyExists_AndNotDuplicateTheUser_OnRepeatedImport()
    {
        var csv = HeaderWithAdmin + "Repeat Council,E06000207,London,LA,E-1,100000,Sam,Jones,LA Admin,sam.jones@repeat.example\n";

        await ImportCsvAsync(csv);
        var second = await ImportCsvAsync(csv);

        second.UpdatedCount.Should().Be(1);
        second.AdminUsersCreatedCount.Should().Be(0);
        second.Rows.Single().AdminUserOutcome.Should().Be("AlreadyExists");
        second.Rows.Single().AdminUserReason.Should().Contain("for this organisation");

        // The exception's own message names the existing user's id; the row must not relay it.
        second.Rows.Single().AdminUserReason.Should().NotContain("under id");

        (await Context.Users.AsNoTracking().CountAsync(x => x.Email == "sam.jones@repeat.example")).Should().Be(1);
    }

    [Fact]
    public async Task ShouldNotCreateAnAdminUser_WhenTheOrganisationRowFailed()
    {
        var csv = HeaderWithAdmin + "Bad Region Council,E06000208,Atlantis,LA,E-1,100000,Alex,Doe,LA Admin,alex.doe@badregion.example\n";

        var result = await ImportCsvAsync(csv);

        result.FailedCount.Should().Be(1);
        result.AdminUsersCreatedCount.Should().Be(0);

        // One problem, reported once — the admin is not failed a second time for a row that never landed. But
        // it is NotAttempted rather than NotProvided: the row did ask for an admin, and the results table
        // shows that address, so "not provided" beside it would read as the file having omitted them.
        result.Rows.Single().AdminUserOutcome.Should().Be("NotAttempted");
        result.Rows.Single().AdminUserEmail.Should().Be("alex.doe@badregion.example");
        result.Rows.Single().AdminUserReason.Should().Contain("did not import");
        (await Context.Users.AsNoTracking().AnyAsync(x => x.Email == "alex.doe@badregion.example")).Should().BeFalse();
    }

    // The likeliest real-world shape of this: re-uploading a file where one row's name collides with an
    // organisation already on file. The row is rejected at the upsert rather than on its cells, and the results
    // table shows that admin's address — so "not provided" beside it would say the file omitted them.
    [Fact]
    public async Task ShouldReportNotAttempted_WhenTheUpsertRejectsTheRow()
    {
        await ImportCsvAsync(HeaderWithAdmin + "Collide Council,E06000220,London,LA,E-1,100000,,,,\n");

        var csv = HeaderWithAdmin + "Collide Council,E06000221,London,LA,E-2,100000,Kim,Fifth,LA Admin,kim.fifth@collide.example\n";
        var result = await ImportCsvAsync(csv);

        result.FailedCount.Should().Be(1);
        result.AdminUsersCreatedCount.Should().Be(0);

        var row = result.Rows.Single();
        row.Reason.Should().Contain("already exists");
        row.AdminUserOutcome.Should().Be("NotAttempted");
        row.AdminUserEmail.Should().Be("kim.fifth@collide.example");
        (await Context.Users.AsNoTracking().AnyAsync(x => x.Email == "kim.fifth@collide.example")).Should().BeFalse();
    }

    // Both halves of the row are wrong. The admin problem is reported rather than swallowed by the
    // organisation's, so one corrected re-upload fixes both instead of two round trips over 153 rows.
    [Fact]
    public async Task ShouldReportTheAdminProblem_EvenWhenTheOrganisationRowAlsoFailed()
    {
        var csv = HeaderWithAdmin + "Both Wrong Council,E06000222,Atlantis,LA,E-1,100000,Lee,Sixth,Manager,lee.sixth@bothwrong.example\n";

        var result = await ImportCsvAsync(csv);

        result.FailedCount.Should().Be(1);
        var row = result.Rows.Single();
        row.Reason.Should().Contain("Atlantis");
        row.AdminUserOutcome.Should().Be("Failed");
        row.AdminUserReason.Should().Contain("Manager");
    }

    [Fact]
    public async Task ShouldCreateAnAdminUser_ForAVcseRow()
    {
        var csv = HeaderWithAdmin + "Vcse Charity,,London,VCSE,E-9,100000,Robin,Green,LA Admin,robin.green@vcse.example\n";

        var result = await ImportCsvAsync(csv);

        result.CreatedCount.Should().Be(1);
        result.AdminUsersCreatedCount.Should().Be(1);

        var org = await Context.Organisations.AsNoTracking().FirstAsync(x => x.Name == "Vcse Charity");
        (await Context.OrganisationUsers.AsNoTracking().AnyAsync(x => x.OrganisationId == org.Id)).Should().BeTrue();
    }

    // The realistic shape of the file — 153 authorities, one admin each. Creating a user mid-loop saves on the
    // same scoped DbContext the row upsert uses, which is precisely where a tracking conflict would strand
    // every later row (the BLUM-7050 failure mode, one layer up).
    [Fact]
    public async Task ShouldCreateAnAdminUser_ForEveryRowOfAMultiRowFile()
    {
        var csv = HeaderWithAdmin +
                  "Multi Council One,E06000210,London,LA,E-1,100000,Ann,One,LA Admin,ann.one@multi.example\n" +
                  "Multi Council Two,E06000211,North West,LA,E-2,200000,Ben,Two,LA Lead,ben.two@multi.example\n" +
                  "Multi Council Three,E06000212,London,LA,E-3,300000,Cat,Three,LA Admin,cat.three@multi.example\n";

        var result = await ImportCsvAsync(csv);

        result.CreatedCount.Should().Be(3);
        result.FailedCount.Should().Be(0);
        result.AdminUsersCreatedCount.Should().Be(3);
        result.Rows.Should().OnlyContain(r => r.AdminUserOutcome == "Created");

        (await Context.Users.AsNoTracking().CountAsync(x => x.Email.EndsWith("@multi.example"))).Should().Be(3);
        (await Context.Users.AsNoTracking().FirstAsync(x => x.Email == "ben.two@multi.example")).Role
            .Should().Be(UserRole.Lead);
    }

    // Email uniqueness is global, so the same address twice in one file is one account plus one reported row —
    // not a duplicate and not a failure that stops the file.
    [Fact]
    public async Task ShouldReportAlreadyExists_WhenTwoRowsShareOneAdminEmail()
    {
        var csv = HeaderWithAdmin +
                  "Shared Email One,E06000213,London,LA,E-1,100000,Dee,Four,LA Admin,shared@both.example\n" +
                  "Shared Email Two,E06000214,London,LA,E-2,100000,Dee,Four,LA Admin,shared@both.example\n";

        var result = await ImportCsvAsync(csv);

        result.CreatedCount.Should().Be(2);
        result.AdminUsersCreatedCount.Should().Be(1);
        result.AdminUsersFailedCount.Should().Be(1);

        // The second organisation gets no user at all, so this is a failure the reader has to see rather than
        // an informational "already exists".
        result.Rows.Select(r => r.AdminUserOutcome).Should().ContainInOrder("Created", "Failed");
        result.Rows[1].AdminUserReason.Should().Contain("another organisation");
        (await Context.Users.AsNoTracking().CountAsync(x => x.Email == "shared@both.example")).Should().Be(1);
    }

    // The allocated_amount guard was the one row-failure path still building its outcome by hand, so a row
    // rejected on money reported its admin as NotProvided and dropped the address entirely.
    [Fact]
    public async Task ShouldReportTheAdmin_WhenTheRowIsRejectedOnAllocatedAmount()
    {
        var csv = HeaderWithAdmin + "Zero Amount Council,E06000230,London,LA,E-1,0,Nia,Seventh,LA Admin,nia.seventh@zero.example\n";

        var result = await ImportCsvAsync(csv);

        result.FailedCount.Should().Be(1);
        var row = result.Rows.Single();
        row.Reason.Should().Contain("allocated_amount must be greater than 0");
        row.AdminUserOutcome.Should().Be("NotAttempted");
        row.AdminUserEmail.Should().Be("nia.seventh@zero.example");
    }

    // Email addresses are not case-sensitive, and neither is the Graph filter the identity service uses. An
    // ordinal duplicate check let both rows through, both received the same subId back, and both wrote a users
    // row — after which UserProfileRepository's SingleOrDefaultAsync on sub_id throws for either account.
    [Fact]
    public async Task ShouldNotCreateTwoAccounts_WhenTwoRowsDifferOnlyInEmailCase()
    {
        var csv = HeaderWithAdmin +
                  "Case One Council,E06000231,London,LA,E-1,100000,Omar,Eighth,LA Admin,O.Eighth@case.example\n" +
                  "Case Two Council,E06000232,London,LA,E-2,100000,Omar,Eighth,LA Admin,o.eighth@case.example\n";

        var result = await ImportCsvAsync(csv);

        result.CreatedCount.Should().Be(2);
        result.AdminUsersCreatedCount.Should().Be(1);
        result.AdminUsersFailedCount.Should().Be(1);
        (await Context.Users.AsNoTracking().CountAsync(x => x.Email.ToLower() == "o.eighth@case.example")).Should().Be(1);
    }

    // A row whose organisation lands but whose admin does not must not read as a clean import: without this
    // count, 153 organisations and 40 rejected admin blocks shows "Failed: 0".
    [Fact]
    public async Task ShouldCountFailedAdmins_SeparatelyFromFailedOrganisations()
    {
        var csv = HeaderWithAdmin +
                  "Count One Council,E06000233,London,LA,E-1,100000,Pia,Ninth,Manager,pia.ninth@count.example\n" +
                  "Count Two Council,E06000234,London,LA,E-2,100000,Rui,Tenth,LA Admin,not-an-email-either\n";

        var result = await ImportCsvAsync(csv);

        result.CreatedCount.Should().Be(2);
        result.FailedCount.Should().Be(0);
        result.AdminUsersCreatedCount.Should().Be(0);
        result.AdminUsersFailedCount.Should().Be(2);
    }

    [Fact]
    public async Task ShouldIncludeTheAdminColumns_InTheDownloadedTemplate()
    {
        var response = await Client.GetAsync("organisations/local-authorities/bulk-import/template");
        response.IsSuccessStatusCode.Should().BeTrue();

        var header = (await response.Content.ReadAsStringAsync()).TrimEnd('\n').Split(',');

        header.Should().ContainInOrder("la_admin_first_name", "la_admin_last_name", "la_admin_access_level", "la_admin_email");
    }

    // A file of ten rows with deliberately mixed fates, imported through the worker pool. Completion order is
    // whatever the pool happens to produce; the response must still read in file order, with each admin
    // outcome against the row that asked for it. Stitching results back by index is the only thing keeping
    // that true, so this is the test that fails if that indexing is ever wrong.
    [Fact]
    public async Task ShouldPreserveRowOrder_WhenUsersAreProvisionedConcurrently()
    {
        var csv = HeaderWithAdmin +
                  "Order One,E06000300,London,LA,E-1,100000,Ada,One,LA Admin,ada.one@order.example\n" +
                  "Order Two,E06000301,London,LA,E-2,100000,,,,\n" +
                  "Order Three,E06000302,London,LA,E-3,100000,Cy,Three,Manager,cy.three@order.example\n" +
                  "Order Four,E06000303,London,LA,E-4,100000,Di,Four,LA Lead,di.four@order.example\n" +
                  "Order Five,E06000304,Atlantis,LA,E-5,100000,Ed,Five,LA Admin,ed.five@order.example\n" +
                  "Order Six,E06000305,London,LA,E-6,100000,Fi,Six,LA Admin,fi.six@order.example\n" +
                  "Order Seven,E06000306,London,LA,E-7,100000,Gil,Seven,LA Admin,not-an-email\n" +
                  "Order Eight,E06000307,London,LA,E-8,100000,Hal,Eight,LA Lead,hal.eight@order.example\n" +
                  "Order Nine,E06000308,London,LA,E-9,100000,Ivy,Nine,LA Admin,ivy.nine@order.example\n" +
                  "Order Ten,E06000309,London,LA,E-10,100000,Jo,Ten,LA Admin,jo.ten@order.example\n";

        var result = await ImportCsvAsync(csv);

        result.Rows.Select(r => r.RowNumber).Should().ContainInOrder(2, 3, 4, 5, 6, 7, 8, 9, 10, 11);
        result.Rows.Select(r => r.Name).Should().ContainInOrder(
            "Order One",
            "Order Two",
            "Order Three",
            "Order Four",
            "Order Five",
            "Order Six",
            "Order Seven",
            "Order Eight",
            "Order Nine",
            "Order Ten");

        result.Rows.Select(r => r.AdminUserOutcome).Should().ContainInOrder(
            "Created",       // Ada
            "NotProvided",   // no admin block
            "Failed",        // unrecognised access level
            "Created",       // Di, LA Lead
            "NotAttempted",  // organisation rejected on region
            "Created",       // Fi
            "Failed",        // malformed email
            "Created",       // Hal
            "Created",       // Ivy
            "Created");      // Jo

        result.AdminUsersCreatedCount.Should().Be(6);
        result.AdminUsersFailedCount.Should().Be(2);
        result.Rows[6].AdminUserEmail.Should().Be("not-an-email");
        result.Rows[4].AdminUserReason.Should().Contain("did not import");
    }

    // The concurrency setting must be a performance dial and nothing else. If a future retune of
    // MaxConcurrentUserCreations could change what an import reports, it stops being safe to touch.
    [Fact]
    public async Task ShouldProduceTheSameOutcomes_AtEveryConcurrencyLevel()
    {
        string Csv(string prefix, string codeSuffix) =>
            HeaderWithAdmin +
            $"{prefix} A,E0600{codeSuffix}1,London,LA,E-1,100000,Ann,A,LA Admin,ann.{prefix.ToLower()}@level.example\n" +
            $"{prefix} B,E0600{codeSuffix}2,London,LA,E-2,100000,,,,\n" +
            $"{prefix} C,E0600{codeSuffix}3,London,LA,E-3,100000,Cal,C,Manager,cal.{prefix.ToLower()}@level.example\n" +
            $"{prefix} D,E0600{codeSuffix}4,Atlantis,LA,E-4,100000,Dot,D,LA Admin,dot.{prefix.ToLower()}@level.example\n" +
            $"{prefix} E,E0600{codeSuffix}5,London,LA,E-5,100000,Eve,E,LA Lead,eve.{prefix.ToLower()}@level.example\n";

        _settings.BulkImport.MaxConcurrentUserCreations = 1;
        var sequential = await ImportCsvAsync(Csv("Seq", "31"));

        _settings.BulkImport.MaxConcurrentUserCreations = 6;
        var concurrent = await ImportCsvAsync(Csv("Par", "32"));

        concurrent.Rows.Select(r => r.Outcome).Should().Equal(sequential.Rows.Select(r => r.Outcome));
        concurrent.Rows.Select(r => r.AdminUserOutcome).Should().Equal(sequential.Rows.Select(r => r.AdminUserOutcome));
        concurrent.AdminUsersCreatedCount.Should().Be(sequential.AdminUsersCreatedCount);
        concurrent.AdminUsersFailedCount.Should().Be(sequential.AdminUsersFailedCount);
    }

    // The reason phase two fans out over distinct addresses only. Two workers holding one email would both
    // clear the duplicate check before either wrote, both be handed the same subId, and both write a users
    // row — after which UserProfileRepository's SingleOrDefaultAsync on sub_id throws for either account.
    [Fact]
    public async Task ShouldNotDuplicateAUser_WhenConcurrentRowsShareAnEmail()
    {
        _settings.BulkImport.MaxConcurrentUserCreations = 8;

        var csv = HeaderWithAdmin + string.Concat(Enumerable.Range(1, 8).Select(i =>
            $"Race Council {i},E060004{i:00},London,LA,E-{i},100000,Sam,Race,LA Admin,sam.race@race.example\n"));

        var result = await ImportCsvAsync(csv);

        result.CreatedCount.Should().Be(8);
        result.AdminUsersCreatedCount.Should().Be(1);
        (await Context.Users.AsNoTracking().CountAsync(x => x.Email.ToLower() == "sam.race@race.example")).Should().Be(1);

        // And nothing shares a subId, which is the failure this guards.
        var subIds = await Context.Users.AsNoTracking().Select(x => x.SubId).ToListAsync();
        subIds.Should().OnlyHaveUniqueItems();
    }

    // Each worker resolves ISender from its own DI scope, and every handler behind it reaches
    // PermissionsService -> IIdentityService, which captures IHttpContextAccessor.HttpContext in its
    // constructor behind a Guard.NotNull. That only works because the accessor is an AsyncLocal-backed
    // singleton and the workers start on the request's execution context. If that ever stops holding, the
    // actor on these rows is the first thing to go wrong.
    [Fact]
    public async Task ShouldResolveTheActingAdmin_InsideWorkerScopes()
    {
        var csv = HeaderWithAdmin +
                  "Actor One,E06000320,London,LA,E-1,100000,Ann,Actor,LA Admin,ann.actor@actor.example\n" +
                  "Actor Two,E06000321,London,LA,E-2,100000,Bob,Actor,LA Lead,bob.actor@actor.example\n" +
                  "Actor Three,E06000322,London,LA,E-3,100000,Cass,Actor,LA Admin,cass.actor@actor.example\n";

        var result = await ImportCsvAsync(csv);
        result.AdminUsersCreatedCount.Should().Be(3);

        var created = await Context.AuditLogs.AsNoTracking()
            .Where(x => x.Action == AuditAction.UserCreated)
            .ToListAsync();

        created.Should().HaveCount(3);
        created.Should().OnlyContain(x => x.ActorUserId == AdminsData.MainAdmin.Id);
        created.Select(x => x.OrganisationId).Should().OnlyHaveUniqueItems();
    }

    // BLUM-8746 — a directory lookup Graph refused used to be thrown, so it reached the importer's catch-all
    // and the row quoted Graph's own message at the reader. It is now returned like a refused create, so the
    // row carries the importer's copy instead. The organisation still lands either way.
    [Fact]
    public async Task ShouldFailTheRowsUser_WithTheImportersOwnCopy_WhenTheDirectoryLookupFails()
    {
        TestGraphContext.FailFindByEmail();

        try
        {
            var csv = HeaderWithAdmin +
                      "Lookup Council,E06000330,London,LA,E-1,100000,Lou,Lookup,LA Admin,lou.lookup@lookup.example\n";

            var result = await ImportCsvAsync(csv);

            result.CreatedCount.Should().Be(1);
            var row = result.Rows.Should().ContainSingle().Subject;
            row.AdminUserOutcome.Should().Be("Failed");
            row.AdminUserReason.Should().Be("The user could not be created. Try this row again, and report it if it keeps failing.");
            (await Context.Users.AsNoTracking().AnyAsync(x => x.Email == "lou.lookup@lookup.example")).Should().BeFalse();
        }
        finally
        {
            TestGraphContext.Reset();
        }
    }

    // The whole point of the change. However slow Graph and SendGrid are, the caller gets a 200 describing
    // what happened rather than a gateway error that loses the record of rows already committed.
    [Fact]
    public async Task ShouldReportRemainingUsersAsNotAttempted_WhenTheBudgetIsSpent()
    {
        _settings.BulkImport.MaxConcurrentUserCreations = 1;
        _settings.BulkImport.UserProvisioningBudgetSeconds = 5;
        TestGraphContext.DelayEachCall(TimeSpan.FromMilliseconds(600));

        var csv = HeaderWithAdmin + string.Concat(Enumerable.Range(1, 12).Select(i =>
            $"Budget Council {i},E060005{i:00},London,LA,E-{i},100000,Bud,Row{i},LA Admin,bud.row{i}@budget.example\n"));

        var result = await ImportCsvAsync(csv);

        // Every organisation lands regardless: phase one is database-only and finishes before the budget
        // can matter.
        result.CreatedCount.Should().Be(12);
        result.FailedCount.Should().Be(0);

        result.Rows.Should().Contain(r => r.AdminUserOutcome == "NotAttempted");
        result.Rows.First(r => r.AdminUserOutcome == "NotAttempted").AdminUserReason
            .Should().Contain("ran out of time").And.Contain("upload the file again");
        result.AdminUsersCreatedCount.Should().BeGreaterThan(0).And.BeLessThan(12);

        // Re-uploading finishes the job: organisations update, the users already created come back as
        // AlreadyExists, and the ones the budget skipped are created.
        TestGraphContext.Reset();
        _settings.BulkImport.UserProvisioningBudgetSeconds = 150;

        var second = await ImportCsvAsync(csv);

        second.UpdatedCount.Should().Be(12);
        second.Rows.Should().NotContain(r => r.AdminUserOutcome == "NotAttempted");
        (await Context.Users.AsNoTracking().CountAsync(x => x.Email.EndsWith("@budget.example"))).Should().Be(12);
    }

    // Without this, "it provisions concurrently now" is an unverified claim: the doubles return instantly, so
    // every other test in this class would pass just as happily against a plain foreach.
    //
    // It asks whether the directory calls overlap, and deliberately times nothing. The first version ran the
    // import at one worker and at six and asserted the time saved. The saving it expected was wrong — the double
    // finds every address, so a user costs one delayed call, not two — which put the threshold exactly on the
    // real figure and failed CI at random. A corrected threshold would still have been a statement about the
    // machine. The gate holds each call until six are in flight together: the pool fills both groups at once,
    // and a plain foreach never brings a second caller, so its first call gives up and says so.
    [Fact]
    public async Task ShouldProvisionConcurrently_NotOneAtATime()
    {
        const int workers = 6;
        const int rows = 12;

        _settings.BulkImport.MaxConcurrentUserCreations = workers;
        var gate = TestGraphContext.HoldCallsUntilInFlight(workers, TimeSpan.FromSeconds(10));

        var csv = HeaderWithAdmin + string.Concat(Enumerable.Range(1, rows).Select(i =>
            $"Pace Council {i},E060006{i:00},London,LA,E-{i},100000,Pace,Row{i},LA Admin,pace.row{i}@pace.example\n"));

        var result = await ImportCsvAsync(csv);

        TestGraphContext.Reset();

        // The reasons, not a count: a group that never filled puts the gate's own message on the row.
        result.Rows.Where(r => r.AdminUserOutcome != "Created").Select(r => r.AdminUserReason).Should().BeEmpty();
        result.AdminUsersCreatedCount.Should().Be(rows);
        gate.GroupsReleased.Should().Be(rows / workers);
    }

    // Abandoning the request must abandon the import, rather than the loop grinding through every remaining
    // row writing "The operation was canceled." against each.
    //
    // Scope of this test, stated because it is easy to over-read: it pins the observable contract, not the
    // explicit ThrowIfCancellationRequested calls in the worker loop and the provisioning catch. It still
    // passes with both of those removed, because a cancelled token propagates out through the delay and the
    // audit append on its own. It is a regression guard against someone swallowing cancellation and carrying
    // on — not evidence that those two lines are load-bearing. They are belt and braces, and this level of
    // test cannot isolate them.
    [Fact]
    public async Task ShouldAbandonTheImport_WhenTheCallerDisconnects()
    {
        _settings.BulkImport.MaxConcurrentUserCreations = 1;
        TestGraphContext.DelayEachCall(TimeSpan.FromMilliseconds(400));

        var csv = HeaderWithAdmin + string.Concat(Enumerable.Range(1, 12).Select(i =>
            $"Abandon Council {i},E060008{i:00},London,LA,E-{i},100000,Abe,Row{i},LA Admin,abe.row{i}@abandon.example\n"));

        using var caller = new CancellationTokenSource(TimeSpan.FromMilliseconds(700));
        using var content = CsvContent(csv, "upload.csv");

        var post = async () => await Client.PostAsync("organisations/local-authorities/bulk-import", content, caller.Token);
        await post.Should().ThrowAsync<OperationCanceledException>();

        TestGraphContext.Reset();

        // Deliberately not asserting an exact organisation count. Whatever the sequential phase managed to
        // write before the token fired stays written — the import is not transactional and never claimed to be
        // — but how far it got in 700ms is a property of the machine, and pinning it to twelve would flake on a
        // loaded CI box running two Testcontainers. What must not happen is the abandoned run carrying on to
        // provision all twelve users.
        (await Context.Users.AsNoTracking().CountAsync(x => x.Email.EndsWith("@abandon.example"))).Should().BeLessThan(12);

        // The discriminating assertion. Merely creating fewer users would also be true of a run that carried
        // on and recorded each remaining row as failed; an import that was abandoned never reaches its audit
        // append at all.
        (await Context.AuditLogs.AsNoTracking().CountAsync(x => x.Action == AuditAction.OrganisationBulkImported)).Should().Be(0);
    }

    // The budget message is a recovery instruction, and it is only true for a row the importer can match again.
    // A VCSE row has no ons_code, so UpsertRowAsync falls through to the create path on a second upload,
    // FindByNameAsync hits the organisation the first upload wrote, and the row fails as a duplicate name —
    // taking its skipped user with it. Sending that admin back to re-upload would strand the user for good.
    [Fact]
    public async Task ShouldTellRowsWithNoOnsCode_ToAddTheUserByHand_WhenTheBudgetIsSpent()
    {
        _settings.BulkImport.MaxConcurrentUserCreations = 1;
        _settings.BulkImport.UserProvisioningBudgetSeconds = 5;
        TestGraphContext.DelayEachCall(TimeSpan.FromMilliseconds(600));

        var csv = HeaderWithAdmin + string.Concat(Enumerable.Range(1, 12).Select(i =>
            $"Vcse Budget {i},,London,VCSE,E-{i},100000,Vic,Row{i},LA Admin,vic.row{i}@vcsebudget.example\n"));

        var result = await ImportCsvAsync(csv);

        result.CreatedCount.Should().Be(12);
        var skipped = result.Rows.Where(r => r.AdminUserOutcome == "NotAttempted").ToList();
        skipped.Should().NotBeEmpty();
        skipped.Should().OnlyContain(r => r.AdminUserReason!.Contains("add this user from its Users page"));
        skipped.Should().OnlyContain(r => !r.AdminUserReason!.Contains("upload the file again to finish"));
    }

    // Without a count, a run that skipped 113 of 153 users reports "created: 40 · failed: 0" and reads like a
    // clean import. The skipped case is the one that needs a second upload, so it is the one that must not be
    // silent.
    [Fact]
    public async Task ShouldCountUsersTheBudgetSkipped_SeparatelyFromFailures()
    {
        _settings.BulkImport.MaxConcurrentUserCreations = 1;
        _settings.BulkImport.UserProvisioningBudgetSeconds = 5;
        TestGraphContext.DelayEachCall(TimeSpan.FromMilliseconds(600));

        var csv = HeaderWithAdmin + string.Concat(Enumerable.Range(1, 12).Select(i =>
            $"Counted Council {i},E060009{i:00},London,LA,E-{i},100000,Cnt,Row{i},LA Admin,cnt.row{i}@counted.example\n"));

        var result = await ImportCsvAsync(csv);

        result.AdminUsersFailedCount.Should().Be(0);
        result.AdminUsersNotAttemptedCount.Should().BeGreaterThan(0);
        result.AdminUsersNotAttemptedCount.Should().Be(result.Rows.Count(r => r.AdminUserOutcome == "NotAttempted"));
        (result.AdminUsersCreatedCount + result.AdminUsersNotAttemptedCount).Should().Be(12);
    }

    // The budget stops users *starting*; on its own that bounds nothing, because a user already running can
    // retry against a throttled directory for minutes. This is the other half: one slow user fails its own row
    // and the rest of the file still completes.
    [Fact]
    public async Task ShouldFailOnlyItsOwnRow_WhenOneUserExceedsThePerUserTimeout()
    {
        _settings.BulkImport.MaxConcurrentUserCreations = 4;
        _settings.BulkImport.UserProvisioningTimeoutSeconds = 5;
        TestGraphContext.DelayEachCall(TimeSpan.FromSeconds(30));

        var csv = HeaderWithAdmin +
                  "Timeout Council,E06000600,London,LA,E-1,100000,Tim,Out,LA Admin,tim.out@timeout.example\n";

        var result = await ImportCsvAsync(csv);

        // The organisation is unaffected, and the row says something the reader can act on rather than
        // "A task was canceled."
        result.CreatedCount.Should().Be(1);
        result.FailedCount.Should().Be(0);
        result.AdminUsersFailedCount.Should().Be(1);

        var row = result.Rows.Single();
        row.AdminUserOutcome.Should().Be("Failed");
        row.AdminUserReason.Should().Contain("took too long").And.Contain("Upload the file again");
        row.AdminUserReason.Should().NotContain("A task was canceled");
    }

    private static MultipartFormDataContent CsvContent(string csv, string fileName)
    {
        var content = new MultipartFormDataContent();
        var streamContent = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        streamContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(streamContent, "file", fileName);
        return content;
    }

    private async Task<HttpResponseMessage> PostCsvAsync(string csv, string fileName = "upload.csv")
    {
        using var content = CsvContent(csv, fileName);
        return await Client.PostAsync("organisations/local-authorities/bulk-import", content);
    }

    private async Task<BulkImportResultDto> ImportCsvAsync(string csv)
    {
        var response = await PostCsvAsync(csv);

        // The body carries the exception on a 500, and reading it here is what turns "expected 200, found 500"
        // into something diagnosable. Guarded so the happy path does not pay for it.
        if (response.StatusCode != HttpStatusCode.OK)
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        }

        return (await response.Content.ReadFromJsonAsync<BulkImportResultDto>())!;
    }

    private static async Task<(string? Error, string? Message)> ReadErrorAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        return (
            json.RootElement.GetProperty("error").GetString(),
            json.RootElement.GetProperty("message").GetString());
    }

    private async Task<bool> HasAnyUserAsync(string onsCode)
    {
        var org = await Context.Organisations.AsNoTracking().FirstAsync(x => x.OnsCode == onsCode);
        return await Context.OrganisationUsers.AsNoTracking().AnyAsync(x => x.OrganisationId == org.Id);
    }

    public Task InitializeAsync()
    {
        // This class relied on the AsyncLocal admin default; once one test calls AuthenticateAs the
        // others need the explicit reset.
        AuthenticateAsAdmin();

        // ApplicationSettings is a singleton, and several tests below retune these. Reset per test so the
        // order tests run in cannot change what they assert. Kept above 1 deliberately: at a single worker
        // every test in this class would exercise the pool vacuously.
        _settings.BulkImport.MaxConcurrentUserCreations = 4;
        _settings.BulkImport.UserProvisioningBudgetSeconds = 150;
        _settings.BulkImport.UserProvisioningTimeoutSeconds = 30;
        TestGraphContext.Reset();

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        Context.AuditLogs.RemoveRange(Context.AuditLogs);

        // Users leak across tests in a way organisations do not: email uniqueness is checked globally, so a
        // left-behind row turns a later test's Created into AlreadyExists. Only the ones this class minted
        // are removed — a blanket Users wipe would also take the DHSC admin that
        // ApplicationDbContextInitialiser.SeedAdminUser writes once at boot, for every later class.
        var organisationUsers = await Context.OrganisationUsers.ToListAsync();
        var importedUserIds = organisationUsers.Select(x => x.UserId).ToList();
        Context.OrganisationUsers.RemoveRange(organisationUsers);
        await SaveChangesAsync();

        Context.Users.RemoveRange(await Context.Users.Where(x => importedUserIds.Contains(x.Id)).ToListAsync());
        Context.OrgFundingAllocations.RemoveRange(Context.OrgFundingAllocations);
        Context.FundingWindows.RemoveRange(Context.FundingWindows);
        Context.Organisations.RemoveRange(Context.Organisations);
        await SaveChangesAsync();
    }
}