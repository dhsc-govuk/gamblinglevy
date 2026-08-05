using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Api.Dtos;
using Domain.AuditLogs;
using Domain.Organisations;
using Domain.ValueObjects;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Tests.Common;
using Xunit;

namespace Api.Tests.Integration.Organisations;

public class LocalAuthorityBulkImportTests(IntegrationTestWebFactory factory)
    : BaseIntegrationTest(factory), IAsyncLifetime
{
    private const string ValidHeader = "name,ons_code,region,type,e_code,allocated_amount\n";

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
            OnboardingStatus.Invited);
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
            _ => throw new InvalidOperationException($"Template column '{c}' has no test value — add one."),
        });

        var result = await ImportCsvAsync(header + string.Join(',', values) + "\n");

        result.CreatedCount.Should().Be(1);
        result.FailedCount.Should().Be(0);
        (await Context.Organisations.AsNoTracking().AnyAsync(x => x.Name == "Template Council")).Should().BeTrue();
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
        response.StatusCode.Should().Be(HttpStatusCode.OK);
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

    public Task InitializeAsync()
    {
        // This class relied on the AsyncLocal admin default; once one test calls AuthenticateAs the
        // others need the explicit reset.
        AuthenticateAsAdmin();
        return Task.CompletedTask;
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