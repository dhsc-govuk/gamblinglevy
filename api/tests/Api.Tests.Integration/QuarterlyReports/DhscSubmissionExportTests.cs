using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using Application.Common.ReportSnapshots;
using Domain.AuditLogs;
using Domain.Collections;
using Domain.DataCollectionForms;
using Domain.Organisations;
using Domain.Submissions;
using Domain.Users;
using Domain.ValueObjects;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Tests.Common;
using Tests.Data;
using Xunit;

namespace Api.Tests.Integration.QuarterlyReports;

// DHSC submission export (BLUM-6100): the window-level export is a ZIP of per-section CSVs aggregated across every org
// (summary.csv keeps the flat per-org summary; activity-key-results.csv / funding-spend.csv / strategic-feedback.csv
// carry each submitter's frozen snapshot). Admin-only on DataCollectionsController; audited (AC5). Plus the per-org
// report ZIP download.
public class DhscSubmissionExportTests(IntegrationTestWebFactory factory) : QuarterlyReportTestBase(factory)
{
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new(JsonSerializerDefaults.Web);

    private Task<HttpResponseMessage> GetWindowExportAsync(Guid collectionId) =>
        Client.GetAsync($"data-collections/{collectionId}/submissions/export");

    private Task<HttpResponseMessage> GetOrgExportAsync(Guid collectionId, Guid organisationId) =>
        Client.GetAsync($"data-collections/{collectionId}/submissions/{organisationId}/export");

    // Reads one CSV entry's text out of the export ZIP. Mirrors ReportExportSnapshotTests' zip reader.
    private static async Task<string> ReadZipEntryAsync(HttpResponseMessage response, string entryName)
    {
        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var entry = archive.GetEntry(entryName);
        entry.Should().NotBeNull("the export ZIP should contain {0}", entryName);
        using var reader = new StreamReader(entry!.Open());
        return await reader.ReadToEndAsync();
    }

    private static async Task<IReadOnlyList<string>> ReadZipEntryNamesAsync(HttpResponseMessage response)
    {
        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        return archive.Entries.Select(e => e.Name).ToList();
    }

    // The concatenated text of every entry in the export ZIP — for "must not appear anywhere in the export" assertions.
    private static async Task<string> ReadWholeZipTextAsync(HttpResponseMessage response)
    {
        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var builder = new StringBuilder();
        foreach (var entry in archive.Entries)
        {
            using var reader = new StreamReader(entry.Open());
            builder.AppendLine(await reader.ReadToEndAsync());
        }

        return builder.ToString();
    }

    // A small frozen snapshot standing in for a finalised report: one activity Q&A, a funding table + summary, and one
    // strategic-feedback Q&A. Serialised with the same Web options the snapshot service uses so it round-trips.
    private static ReportSnapshotDocument SampleSnapshot() => new(
        [
            new ReportSnapshotSection(
                "Public Health Officer post",
                [new ReportSnapshotRow("What did you achieve this quarter?", "Recruited a public health officer")]),
        ],
        new ReportSnapshotFunding(
            ["Activity", "Levy spent this quarter (£)", "Total spent so far (£)", "Forecast Q2 (£)"],
            [["Public Health Officer post", "150.00", "150.00", "40.00"]],
            [
                new ReportSnapshotRow("Total grant allocation", "1000.00"),
                new ReportSnapshotRow("Spend to date", "150.00"),
            ]),
        [
            new ReportSnapshotSection(
                "Strategic feedback",
                [new ReportSnapshotRow("How is delivery progressing?", "On track for the year")]),
        ]);

    // Seeds a finalised submission for an org plus some Actual + Forecast spend lines, so the export can sum the
    // "total reported spend" (Actual only). Optionally attaches a frozen report snapshot so the section files have
    // content. Mirrors DhscSubmissionStatusTests.SeedSubmittedSubmissionAsync.
    // A user's email and a submission's reference number are both unique, so every seeded submitter takes the next
    // number in sequence — a cross-organisation export test seeds more than one.
    private int seededSubmitters;

    private async Task SeedSubmittedSubmissionWithSpendAsync(
        Guid collectionId,
        Organisation org,
        decimal actualOne,
        decimal actualTwo,
        decimal forecast,
        ReportSnapshotDocument? snapshot = null)
    {
        var sequence = ++seededSubmitters;
        var submitter = User.New(
            UserId.New(),
            new Name("Julie", "Smith"),
            $"julie.smith{sequence}@example.gov.uk",
            new SubId(Guid.NewGuid()),
            UserStatus.Active,
            UserRole.Lead);
        await Context.Users.AddAsync(submitter);

        var submission = Submission.Start(
            SubmissionId.New(),
            new CollectionId(collectionId),
            org.Id,
            Array.Empty<(FormModuleId ModuleId, string ModuleCode)>(),
            submitter.Id);
        submission.Submit($"202701-{sequence:0000}", submitter.Id);
        if (snapshot is not null)
        {
            submission.AttachReportSnapshot(JsonSerializer.Serialize(snapshot, SnapshotJsonOptions));
        }

        await Context.Submissions.AddAsync(submission);

        await Context.SubmissionSpendEntries.AddAsync(
            SubmissionSpendEntry.Actual(SubmissionSpendEntryId.New(), submission.Id, null, actualOne, submitter.Id));
        await Context.SubmissionSpendEntries.AddAsync(
            SubmissionSpendEntry.Actual(SubmissionSpendEntryId.New(), submission.Id, null, actualTwo, submitter.Id));
        await Context.SubmissionSpendEntries.AddAsync(
            SubmissionSpendEntry.Forecast(SubmissionSpendEntryId.New(), submission.Id, null, 2, forecast, submitter.Id));

        await SaveChangesAsync();
    }

    // Seeds a finalised submission whose frozen snapshot JSON is valid JSON but NOT a snapshot document (the column is
    // jsonb, so syntactically-invalid text can't be stored — a wrong-shape value is how deserialisation fails), to
    // prove that one snapshot the reader cannot decode does not fail the whole cross-org export.
    private async Task SeedSubmittedSubmissionWithRawSnapshotAsync(Guid collectionId, Organisation org, string rawSnapshotJson)
    {
        var sequence = ++seededSubmitters;
        var submitter = User.New(
            UserId.New(),
            new Name("Ivy", "Invalid"),
            $"ivy.invalid{sequence}@example.gov.uk",
            new SubId(Guid.NewGuid()),
            UserStatus.Active,
            UserRole.Lead);
        await Context.Users.AddAsync(submitter);

        var submission = Submission.Start(
            SubmissionId.New(),
            new CollectionId(collectionId),
            org.Id,
            Array.Empty<(FormModuleId ModuleId, string ModuleCode)>(),
            submitter.Id);
        submission.Submit($"202701-{sequence:0000}", submitter.Id);
        submission.AttachReportSnapshot(rawSnapshotJson);
        await Context.Submissions.AddAsync(submission);

        await SaveChangesAsync();
    }

    // Seeds a DRAFT (never-submitted) submission for an org plus Actual spend lines — to prove draft spend does NOT
    // leak into the export's "total reported spend" (Actual rows are written while a report is still a draft).
    private async Task SeedDraftSubmissionWithSpendAsync(
        Guid collectionId,
        Organisation org,
        decimal actualOne,
        decimal actualTwo)
    {
        var submitter = User.New(
            UserId.New(),
            new Name("Drew", "Draft"),
            "drew.draft@example.gov.uk",
            new SubId(Guid.NewGuid()),
            UserStatus.Active,
            UserRole.Lead);
        await Context.Users.AddAsync(submitter);

        // Intentionally NOT submitted — a draft with recorded actual spend.
        var submission = Submission.Start(
            SubmissionId.New(),
            new CollectionId(collectionId),
            org.Id,
            Array.Empty<(FormModuleId ModuleId, string ModuleCode)>(),
            submitter.Id);
        await Context.Submissions.AddAsync(submission);

        await Context.SubmissionSpendEntries.AddAsync(
            SubmissionSpendEntry.Actual(SubmissionSpendEntryId.New(), submission.Id, null, actualOne, submitter.Id));
        await Context.SubmissionSpendEntries.AddAsync(
            SubmissionSpendEntry.Actual(SubmissionSpendEntryId.New(), submission.Id, null, actualTwo, submitter.Id));

        await SaveChangesAsync();
    }

    [Fact]
    public async Task ExportSubmissions_ListsOneRowPerOrgWithStatusSpendAndPeriod()
    {
        var collectionId = await CreateOpenCollectionAsync(localAuthorityIds: [MainOrg.Id.Value, SecondOrg.Id.Value]);
        await SeedSubmittedSubmissionWithSpendAsync(collectionId, MainOrg, 100m, 50m, 999m);

        AuthenticateAsAdmin();
        var response = await GetWindowExportAsync(collectionId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");

        var csv = await ReadZipEntryAsync(response, "summary.csv");
        var lines = csv.Split("\r\n");

        lines[0].Should().Be("Organisation,Status,Submission date,Total reported spend (£),Financial year,Quarter");
        lines.Should().HaveCount(3); // header + the two assigned orgs

        // The submitted org: Completed, Actual spend summed (the 999 forecast is excluded), FY + quarter.
        var completedLine = lines.Single(l => l.Contains(",Completed,"));
        completedLine.Should().Contain(",150.00,2026/27,1");

        // The non-submitter: Not started, blank date, zero spend.
        var notStartedLine = lines.Single(l => l.Contains(",Not started,"));
        notStartedLine.Should().Contain(",,0.00,2026/27,1");
    }

    [Fact]
    public async Task ExportSubmissions_ExcludesDraftSpend()
    {
        // MainOrg has a finalised submission (its actuals count); SecondOrg has only a DRAFT with recorded actuals,
        // which must NOT be reported as spend.
        var collectionId = await CreateOpenCollectionAsync(localAuthorityIds: [MainOrg.Id.Value, SecondOrg.Id.Value]);
        await SeedSubmittedSubmissionWithSpendAsync(collectionId, MainOrg, 100m, 50m, 0m);
        await SeedDraftSubmissionWithSpendAsync(collectionId, SecondOrg, 200m, 25m);

        AuthenticateAsAdmin();
        var csv = await ReadZipEntryAsync(await GetWindowExportAsync(collectionId), "summary.csv");
        var lines = csv.Split("\r\n");

        // Submitted org: Completed + summed actuals.
        lines.Single(l => l.Contains(",Completed,")).Should().Contain(",150.00,");

        // Draft org: In progress, blank submission date, zero reported spend (the 225 draft actuals are excluded).
        lines.Single(l => l.Contains(",In progress,")).Should().Contain(",,0.00,");
    }

    [Fact]
    public async Task ExportSubmissions_OrgWithSubmissionNotAssigned_IncludedWithSpend()
    {
        // Collection assigned to MainOrg only; SecondOrg has a finalised submission with spend but is not assigned
        // (e.g. de-assigned after submitting). It must still appear in the export with its Completed status and spend.
        var collectionId = await CreateOpenCollectionAsync(localAuthorityIds: [MainOrg.Id.Value]);
        await SeedSubmittedSubmissionWithSpendAsync(collectionId, SecondOrg, 100m, 50m, 0m);

        AuthenticateAsAdmin();
        var csv = await ReadZipEntryAsync(await GetWindowExportAsync(collectionId), "summary.csv");
        var lines = csv.Split("\r\n");

        lines.Should().HaveCount(3); // header + MainOrg (assigned, not started) + SecondOrg (submission, not assigned)
        csv.Should().Contain(SecondOrg.Name);
        lines.Single(l => l.Contains(",Completed,")).Should().Contain(",150.00,");
    }

    [Fact]
    public async Task ExportSubmissions_ReturnsZipWithSummaryAndSectionFiles()
    {
        var collectionId = await CreateOpenCollectionAsync();
        await SeedSubmittedSubmissionWithSpendAsync(collectionId, MainOrg, 100m, 0m, 0m, SampleSnapshot());

        AuthenticateAsAdmin();
        var response = await GetWindowExportAsync(collectionId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");

        var entries = await ReadZipEntryNamesAsync(response);
        entries.Should().BeEquivalentTo(
            "summary.csv", "activity-key-results.csv", "funding-spend.csv", "funding-summary.csv", "strategic-feedback.csv");
    }

    [Fact]
    public async Task ExportSubmissions_SectionFiles_CarrySubmitterSnapshotDataWithOrgColumns()
    {
        // MainOrg submits (with a frozen snapshot); SecondOrg is assigned but never submits.
        var collectionId = await CreateOpenCollectionAsync(localAuthorityIds: [MainOrg.Id.Value, SecondOrg.Id.Value]);
        await SeedSubmittedSubmissionWithSpendAsync(collectionId, MainOrg, 150m, 0m, 0m, SampleSnapshot());

        AuthenticateAsAdmin();
        var response = await GetWindowExportAsync(collectionId);

        var activity = await ReadZipEntryAsync(response, "activity-key-results.csv");
        activity.Split("\r\n")[0].Should().Be("Organisation,ONS code,E-code,Activity,Question,Answer");
        activity.Should().Contain("Main Council,E06000001,E1000001,Public Health Officer post");
        activity.Should().Contain("Recruited a public health officer");

        var funding = await ReadZipEntryAsync(response, "funding-spend.csv");
        funding.Should().Contain("Forecast Q2 (£)");
        funding.Should().Contain("Main Council,E06000001,E1000001,Public Health Officer post,150.00,150.00,40.00");

        // The per-organisation totals are a different grain, so they live in their own file rather than under the
        // per-activity money columns.
        var fundingSummary = (await ReadZipEntryAsync(response, "funding-summary.csv")).Split("\r\n");
        fundingSummary[0].Should().Be("Organisation,ONS code,E-code,Summary,Amount (£)");
        fundingSummary.Should().Contain("Main Council,E06000001,E1000001,Total grant allocation,1000.00");
        funding.Should().NotContain("Total grant allocation");

        var feedback = await ReadZipEntryAsync(response, "strategic-feedback.csv");
        feedback.Split("\r\n")[0].Should().Be("Organisation,ONS code,E-code,Section,Question,Answer");
        feedback.Should().Contain("On track for the year");

        // The non-submitter is in the summary but has no rows in any section file (no snapshot to decompose).
        var summary = await ReadZipEntryAsync(response, "summary.csv");
        summary.Should().Contain(SecondOrg.Name);
        activity.Should().NotContain(SecondOrg.Name);
        feedback.Should().NotContain(SecondOrg.Name);
    }

    [Fact]
    public async Task ExportSubmissions_NoSubmitters_SectionFilesAreHeaderOnly()
    {
        // Assigned org that never submits: the section files are valid but carry only their headers.
        var collectionId = await CreateOpenCollectionAsync();

        AuthenticateAsAdmin();
        var response = await GetWindowExportAsync(collectionId);

        response.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");

        var activity = (await ReadZipEntryAsync(response, "activity-key-results.csv")).Split("\r\n");
        activity.Should().ContainSingle().Which.Should().Be("Organisation,ONS code,E-code,Activity,Question,Answer");

        var funding = (await ReadZipEntryAsync(response, "funding-spend.csv")).Split("\r\n");
        funding.Should().ContainSingle()
            .Which.Should().Be("Organisation,ONS code,E-code,Activity,Levy spent this quarter (£),Total spent so far (£)");

        var fundingSummary = (await ReadZipEntryAsync(response, "funding-summary.csv")).Split("\r\n");
        fundingSummary.Should().ContainSingle()
            .Which.Should().Be("Organisation,ONS code,E-code,Summary,Amount (£)");
    }

    [Fact]
    public async Task ExportSubmissions_SectionFiles_AreRectangular()
    {
        // Every aggregated file is one rectangle: a single header and rows of exactly that width. funding-spend.csv
        // used to carry the per-organisation summary metrics under a mid-file sub-header, which put each amount under
        // a per-activity money column ("Levy spent this quarter (£)") — summing that column double-counted the grant
        // allocations and totals of every organisation in the window.
        var collectionId = await CreateOpenCollectionAsync(localAuthorityIds: [MainOrg.Id.Value, SecondOrg.Id.Value]);
        await SeedSubmittedSubmissionWithSpendAsync(collectionId, MainOrg, 150m, 0m, 0m, SampleSnapshot());
        await SeedSubmittedSubmissionWithSpendAsync(collectionId, SecondOrg, 200m, 0m, 0m, SampleSnapshot());

        AuthenticateAsAdmin();
        var response = await GetWindowExportAsync(collectionId);

        foreach (var name in new[] { "summary.csv", "activity-key-results.csv", "funding-spend.csv", "funding-summary.csv", "strategic-feedback.csv" })
        {
            var lines = (await ReadZipEntryAsync(response, name)).Split("\r\n");
            var width = CountCells(lines[0]);
            lines.Should().OnlyContain(line => CountCells(line) == width, $"every row of {name} must match its header width");
        }

        // Both organisations' metrics are present, each under the summary file's own columns.
        var fundingSummary = await ReadZipEntryAsync(response, "funding-summary.csv");
        fundingSummary.Should().Contain("Main Council,E06000001,E1000001,Total grant allocation,1000.00");
        fundingSummary.Should().Contain($"{SecondOrg.Name},");
    }

    // Cell count for a CSV line. The frozen snapshot cells under test carry no embedded commas or quotes, so a plain
    // split is enough — this is a shape check, not a CSV parser.
    private static int CountCells(string line) => line.Split(',').Length;

    [Fact]
    public async Task ExportSubmissions_CorruptSnapshot_DoesNotFailExport_OrgOmittedFromSections()
    {
        // A finalised submission whose frozen snapshot JSON is corrupt must not 500 the whole window export: the org
        // stays in the summary (it did submit) but contributes no rows to the section files.
        var collectionId = await CreateOpenCollectionAsync();
        await SeedSubmittedSubmissionWithRawSnapshotAsync(collectionId, MainOrg, "\"not a snapshot document\"");

        AuthenticateAsAdmin();
        var response = await GetWindowExportAsync(collectionId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadZipEntryAsync(response, "summary.csv")).Should().Contain("Main Council");
        (await ReadZipEntryAsync(response, "activity-key-results.csv")).Split("\r\n")
            .Should().ContainSingle("a corrupt snapshot yields no section rows");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"activitySections":null,"funding":null,"feedbackSections":null}""")]
    [InlineData("""{"activitySections":[{"title":"Post","rows":null}],"funding":{"header":null,"rows":null,"summary":null},"feedbackSections":[]}""")]
    public async Task ExportSubmissions_PartialSnapshot_DoesNotFailExport(string rawSnapshotJson)
    {
        // A snapshot whose members are absent or explicitly null — a legacy document written before a member existed,
        // or a partially-written one. Every collection member on ReportSnapshotDocument coalesces null to empty, so the
        // export formats it as "no rows" instead of dereferencing null. This pins that the invariant survives a JSON
        // round-trip: the records are deserialised through their primary constructor, and an explicitly-null member
        // must not overwrite the coalesced value afterwards.
        var collectionId = await CreateOpenCollectionAsync();
        await SeedSubmittedSubmissionWithRawSnapshotAsync(collectionId, MainOrg, rawSnapshotJson);

        AuthenticateAsAdmin();
        var response = await GetWindowExportAsync(collectionId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadZipEntryAsync(response, "summary.csv")).Should().Contain("Main Council");
        (await ReadZipEntryAsync(response, "activity-key-results.csv")).Split("\r\n")
            .Should().ContainSingle("a snapshot with no usable rows contributes only the header");

        // The same document must survive the per-organisation export, which deserialises it through the other path.
        (await GetOrgExportAsync(collectionId, MainOrg.Id.Value)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ExportSubmissions_MaturityAssessmentCollection_Returns404()
    {
        // Maturity assessments have no submission flow, so there is nothing to export and the export must not be audited.
        var collectionId = await CreateMaturityAssessmentCollectionAsync(localAuthorityIds: [MainOrg.Id.Value]);

        AuthenticateAsAdmin();
        (await GetWindowExportAsync(collectionId)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var entries = await Context.AuditLogs.AsNoTracking()
            .Where(a => a.Action == AuditAction.SubmissionExported)
            .ToListAsync();
        entries.Should().BeEmpty();
    }

    [Fact]
    public async Task ExportSubmissions_WritesAuditLogEntry()
    {
        var collectionId = await CreateOpenCollectionAsync();

        AuthenticateAsAdmin();
        (await GetWindowExportAsync(collectionId)).StatusCode.Should().Be(HttpStatusCode.OK);

        var entries = await Context.AuditLogs.AsNoTracking()
            .Where(a => a.Action == AuditAction.SubmissionExported)
            .ToListAsync();

        entries.Should().ContainSingle();
        entries[0].TargetType.Should().Be(AuditTargetType.Collection);
        entries[0].TargetId.Should().Be(collectionId);
        entries[0].ActorUserId.Should().Be(AdminsData.MainAdmin.Id);
        // After is stored as jsonb (Postgres reorders keys and re-spaces), so match the format value only.
        entries[0].After.Should().Contain("zip");
    }

    [Fact]
    public async Task ExportSubmissions_AllLasCollection_IncludesLasExcludesVcse()
    {
        var collectionId = await CreateOpenAllLasCollectionAsync();

        AuthenticateAsAdmin();
        var response = await GetWindowExportAsync(collectionId);

        var summary = await ReadZipEntryAsync(response, "summary.csv");
        summary.Should().Contain(MainOrg.Name);
        summary.Should().Contain(SecondOrg.Name);

        // A VCSE org must not leak into ANY file of an all-LAs export — check every entry, not just the summary.
        (await ReadWholeZipTextAsync(response)).Should().NotContain(VcseOrg.Name);
    }

    [Fact]
    public async Task ExportSubmissions_UnknownCollection_Returns404()
    {
        AuthenticateAsAdmin();

        (await GetWindowExportAsync(Guid.NewGuid())).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Export_AsNonAdmin_Returns403()
    {
        var collectionId = await CreateOpenCollectionAsync();
        await SeedSubmittedSubmissionWithSpendAsync(collectionId, MainOrg, 100m, 0m, 0m);

        AuthenticateAsMainOrgLead();

        (await GetWindowExportAsync(collectionId)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await GetOrgExportAsync(collectionId, MainOrg.Id.Value)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ExportOrgReport_SubmittedOrg_ReturnsZip()
    {
        var collectionId = await CreateOpenCollectionAsync();
        await SeedSubmittedSubmissionWithSpendAsync(collectionId, MainOrg, 100m, 0m, 0m);

        AuthenticateAsAdmin();
        var response = await GetOrgExportAsync(collectionId, MainOrg.Id.Value);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");
        (await response.Content.ReadAsByteArrayAsync()).Should().NotBeEmpty();
    }

    [Fact]
    public async Task ExportOrgReport_NoSubmissionOrUnassigned_Returns404()
    {
        // Collection assigned to MainOrg only, no submission seeded.
        var collectionId = await CreateOpenCollectionAsync();

        AuthenticateAsAdmin();

        // Assigned but has not submitted.
        (await GetOrgExportAsync(collectionId, MainOrg.Id.Value)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        // Not assigned to this collection.
        (await GetOrgExportAsync(collectionId, VcseOrg.Id.Value)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}