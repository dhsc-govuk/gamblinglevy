using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using Application.Common.Csv;
using Application.Common.Features;
using Application.Common.ReportSnapshots;
using Domain.AuditLogs;
using Domain.Collections;
using Domain.DataCollectionForms;
using Domain.Funding;
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
// (summary.csv keeps the flat per-org summary; activity-results.csv / funding-spend.csv / strategic-feedback.csv
// carry each submitter's frozen snapshot). Admin-only on DataCollectionsController; audited (AC5). Plus the per-org
// report ZIP download.
public class DhscSubmissionExportTests(IntegrationTestWebFactory factory) : QuarterlyReportTestBase(factory)
{
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new(JsonSerializerDefaults.Web);

    private Task<HttpResponseMessage> GetWindowExportAsync(Guid collectionId) =>
        Client.GetAsync($"data-collections/{collectionId}/submissions/export");

    // The BLUM-7682 region filter. A null region omits the parameter entirely (the pre-filter URL); a non-null one is
    // sent verbatim, including the empty string, so a test can prove "?region=" still means every organisation.
    private Task<HttpResponseMessage> GetWindowExportAsync(Guid collectionId, string? region) =>
        Client.GetAsync(region is null
            ? $"data-collections/{collectionId}/submissions/export"
            : $"data-collections/{collectionId}/submissions/export?region={Uri.EscapeDataString(region)}");

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

    // The fixed Activity ID rendered in SampleSnapshot's funding row — a real Activity is never seeded for these
    // tests (the snapshot is hand-built, not produced by ReportSnapshotService), so any stable GUID string works.
    private static readonly Guid SampleActivityId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    // A small frozen snapshot standing in for a finalised report: one activity Q&A, a funding table + totals, and one
    // strategic-feedback Q&A. Serialised with the same Web options the snapshot service uses so it round-trips.
    // Mirrors the fixed Q1-4 actual / Q2-4 forecast shape ReportSnapshotService.BuildFunding produces (BLUM-7130 CSV
    // review): Q1 actual £150, Q2 forecast £40, nothing yet recorded for Q3/Q4 (blank, not zero).
    private static ReportSnapshotDocument SampleSnapshot() => new(
        [
            new ReportSnapshotSection(
                "Public Health Officer post",
                [new ReportSnapshotRow("What did you achieve this quarter?", "Recruited a public health officer")])
        ],
        new ReportSnapshotFunding(
            ReportSnapshotCsv.FundingHeader,
            [
                [
                    "Public Health Officer post", "Public health", SampleActivityId.ToString(), "150.00", "40.00", string.Empty,
                    string.Empty, string.Empty, string.Empty, string.Empty, "150.00", "190.00"
                ]
            ],
            [
                new ReportSnapshotRow("Total grant allocation", "1000.00"),
                new ReportSnapshotRow("Total spend to date", "150.00")
            ],
            new ReportSnapshotFundingTotals(
                AnnualAllocation: "1000.00",
                Q1TotalActual: "150.00",
                Q2Forecast: "40.00",
                Q2TotalActual: string.Empty,
                Q3Forecast: string.Empty,
                Q3TotalActual: string.Empty,
                Q4Forecast: string.Empty,
                Q4TotalActual: string.Empty,
                TotalSpendToDate: "150.00",
                RemainingBudget: "850.00",
                TotalForecast: "40.00",
                ProjectedYearEnd: "190.00",
                ProjectedVariance: "810.00")),
        [
            new ReportSnapshotSection(
                "Strategic feedback",
                [new ReportSnapshotRow("How is delivery progressing?", "On track for the year")])
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
            UserRole.Lead,
            DateTime.UtcNow);
        await Context.Users.AddAsync(submitter);

        var submission = Submission.Start(
            SubmissionId.New(),
            new CollectionId(collectionId),
            org.Id,
            await AllModuleSectionsAsync(),
            submitter.Id,
            DateTime.UtcNow);
        submission.Submit(TestFinancialYears.Reference(TestFinancialYears.Current, 1, sequence), submitter.Id, DateTime.UtcNow);
        if (snapshot is not null)
        {
            submission.AttachReportSnapshot(JsonSerializer.Serialize(snapshot, SnapshotJsonOptions));
        }

        await Context.Submissions.AddAsync(submission);

        await Context.SubmissionSpendEntries.AddAsync(
            SubmissionSpendEntry.Actual(SubmissionSpendEntryId.New(), submission.Id, null, actualOne, submitter.Id, DateTime.UtcNow));
        await Context.SubmissionSpendEntries.AddAsync(
            SubmissionSpendEntry.Actual(SubmissionSpendEntryId.New(), submission.Id, null, actualTwo, submitter.Id, DateTime.UtcNow));
        await Context.SubmissionSpendEntries.AddAsync(
            SubmissionSpendEntry.Forecast(SubmissionSpendEntryId.New(), submission.Id, null, 2, forecast, submitter.Id, DateTime.UtcNow));

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
            UserRole.Lead,
            DateTime.UtcNow);
        await Context.Users.AddAsync(submitter);

        var submission = Submission.Start(
            SubmissionId.New(),
            new CollectionId(collectionId),
            org.Id,
            await AllModuleSectionsAsync(),
            submitter.Id,
            DateTime.UtcNow);
        submission.Submit(TestFinancialYears.Reference(TestFinancialYears.Current, 1, sequence), submitter.Id, DateTime.UtcNow);
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
            UserRole.Lead,
            DateTime.UtcNow);
        await Context.Users.AddAsync(submitter);

        // Intentionally NOT submitted — a draft with recorded actual spend.
        var submission = Submission.Start(
            SubmissionId.New(),
            new CollectionId(collectionId),
            org.Id,
            Array.Empty<(FormModuleId ModuleId, string ModuleCode)>(),
            submitter.Id,
            DateTime.UtcNow);
        await Context.Submissions.AddAsync(submission);

        await Context.SubmissionSpendEntries.AddAsync(
            SubmissionSpendEntry.Actual(SubmissionSpendEntryId.New(), submission.Id, null, actualOne, submitter.Id, DateTime.UtcNow));
        await Context.SubmissionSpendEntries.AddAsync(
            SubmissionSpendEntry.Actual(SubmissionSpendEntryId.New(), submission.Id, null, actualTwo, submitter.Id, DateTime.UtcNow));

        await SaveChangesAsync();
    }

    // Seeds (find-or-create) a funding allocation for an organisation against the given financial year — mirrors
    // FundingSpendSummaryTests.SeedCurrentWindowAllocationAsync, but keyed by an explicit financial year so a test
    // can seed the year BESIDE the collection's (the next-year allocation column) as well as its own. Callers pass
    // TestFinancialYears.Current for the collection's own year rather than a literal.
    private async Task SeedFundingAllocationAsync(Organisation org, string financialYear, decimal amount)
    {
        var window = await Context.FundingWindows.FirstOrDefaultAsync(w => w.FinancialYear == financialYear);
        if (window is null)
        {
            window = FundingWindow.New(FundingWindowId.New(), financialYear, $"Annual Allocation {financialYear}", DateTime.UtcNow);
            await Context.FundingWindows.AddAsync(window);
            await SaveChangesAsync();
        }

        await Context.OrgFundingAllocations.AddAsync(OrgFundingAllocation.New(
            OrgFundingAllocationId.New(), org.Id, window.Id, amount, UserId.New(), DateTime.UtcNow));
        await SaveChangesAsync();
    }

    // BLUM-7608 AC3: funding-summary.csv names the year the annual allocation belongs to, and carries the
    // upcoming year's allocation beside it. Drives the REAL flow (author -> submit -> freeze -> export) rather
    // than a hand-built snapshot, because the values come from what ReportSnapshotService computes.
    [Fact]
    public async Task ExportSubmissions_FundingSummary_NamesTheAllocationYearAndCarriesTheUpcomingYear()
    {
        var financialYear = TestFinancialYears.Current;
        AuthenticateAsMainOrgLead();
        var collectionId = await CreateOpenQuarterCollectionAsync(1, "Allocation years Q1", financialYear);
        var activity = await CreateRnaActivityAsync();
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);
        await SeedFundingAllocationAsync(MainOrg, financialYear, 10_000m);
        await SeedFundingAllocationAsync(MainOrg, TestFinancialYears.Next(financialYear), 12_000m);

        await CompleteActivityReportAsync(collectionId, activity.Id);
        await CompleteFundingSpendSectionAsync(collectionId, perActivityActual: 100m, otherActual: 0m, perQuarterForecast: 0m, otherForecast: 0m);
        await CompleteStrategicFeedbackSectionAsync(collectionId);
        (await Client.PostAsync($"quarterly-report/{collectionId}/submission/submit", null)).EnsureSuccessStatusCode();

        AuthenticateAsAdmin();
        var row = await ReadFundingSummaryRowAsync(await GetWindowExportAsync(collectionId), MainOrg.Name);

        row["Annual allocation (£)"].Should().Be("10000.00");
        row["Annual allocation financial year"].Should().Be(financialYear);
        row["Next year annual allocation (£)"].Should().Be("12000.00");
        row["Next year financial year"].Should().Be(TestFinancialYears.Next(financialYear));
    }

    // A snapshot frozen before those columns existed deserialises with them null. The file must stay ONE
    // rectangle across every organisation in the export, so the missing cells render blank rather than being
    // dropped — the same contract Totals itself was introduced under.
    [Fact]
    public async Task ExportSubmissions_FundingSummary_RendersBlankAllocationYearCells_ForAPreExistingSnapshot()
    {
        var collectionId = await CreateOpenCollectionAsync(localAuthorityIds: [MainOrg.Id.Value]);
        await SeedSubmittedSubmissionWithSpendAsync(collectionId, MainOrg, 100m, 50m, 999m, SampleSnapshot());

        AuthenticateAsAdmin();
        var response = await GetWindowExportAsync(collectionId);
        var lines = (await ReadZipEntryAsync(response, "funding-summary.csv")).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        var row = await ReadFundingSummaryRowAsync(response, MainOrg.Name);

        row["Annual allocation (£)"].Should().Be("1000.00", "the frozen snapshot still carries the amount");
        row["Annual allocation financial year"].Should().BeEmpty();
        row["Next year annual allocation (£)"].Should().BeEmpty();
        row["Next year financial year"].Should().BeEmpty();

        lines[1].Split(',').Should().HaveSameCount(lines[0].Split(','), "the file has to stay one rectangle");
    }

    [Fact]
    public async Task ExportSubmissions_OverspentOrganisation_RendersNegativeMoneyWithoutTrippingTheFormulaGuard()
    {
        // RemainingBudget/ProjectedVariance are derived aggregates that CAN go negative (an organisation
        // overspending its allocation) even though every individual spend entry is guarded non-negative at the
        // domain level. CsvWriter.Escape treats a leading '-' as a possible formula-injection attempt and prefixes
        // it with a single quote, which would silently turn every overspending organisation's figures into Excel
        // text — exactly the organisations these columns exist to surface. Money() must never render a leading '-'.
        var financialYear = TestFinancialYears.Current;
        AuthenticateAsMainOrgLead();
        var collectionId = await CreateOpenCollectionAsync();
        var activity = await CreateRnaActivityAsync();
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);
        await SeedFundingAllocationAsync(MainOrg, financialYear, 50m);

        await CompleteActivityReportAsync(collectionId, activity.Id);
        await CompleteFundingSpendSectionAsync(collectionId, perActivityActual: 200m, otherActual: 0m, perQuarterForecast: 0m, otherForecast: 0m);
        await CompleteStrategicFeedbackSectionAsync(collectionId);
        (await Client.PostAsync($"quarterly-report/{collectionId}/submission/submit", null)).EnsureSuccessStatusCode();

        AuthenticateAsAdmin();
        var fundingSummary = await ReadZipEntryAsync(await GetWindowExportAsync(collectionId), "funding-summary.csv");

        fundingSummary.Should().Contain("(150.00)", "£50 allocation minus £200 spent renders as accounting-negative, not a leading '-'");
        fundingSummary.Should().NotContain("-150.00");
    }

    // --- BLUM-7607: the funding numbers a DHSC analyst adds up must reconcile ---------------------------------
    //
    // These are the only tests that drive the REAL report flow (author -> submit -> freeze -> export) and then assert
    // the funding VALUES in the CSV. Everything else in this class asserts shape against a hand-built SampleSnapshot,
    // which cannot catch a defect in what ReportSnapshotService computes.

    [Fact]
    public async Task ExportSubmissions_FundingSummary_QuarterlyActualsSumToTotalSpendToDate()
    {
        var financialYear = TestFinancialYears.Current;
        var (_, q2, _) = await SeedTwoQuarterReportWithADeletedActivityAsync(financialYear);

        AuthenticateAsAdmin();
        var row = await ReadFundingSummaryRowAsync(await GetWindowExportAsync(q2), MainOrg.Name);

        var quarterlyTotal = ParseMoney(row["Q1 total actual (£)"])
            + ParseMoney(row["Q2 total actual (£)"])
            + ParseMoney(row["Q3 total actual (£)"])
            + ParseMoney(row["Q4 total actual (£)"]);

        quarterlyTotal.Should().Be(
            ParseMoney(row["Total spend to date (£)"]),
            "the quarterly actual columns are the breakdown OF the total, so they have to add up to it");

        // The figures themselves: Q1 recorded £100 + £100 + £20 (one of those activities was then deleted),
        // Q2 recorded £100 + £20 against what was left.
        ParseMoney(row["Q1 total actual (£)"]).Should().Be(220m);
        ParseMoney(row["Q2 total actual (£)"]).Should().Be(120m);
        ParseMoney(row["Total spend to date (£)"]).Should().Be(340m);

        // Forecasted year-end is spend to date plus what is still forecast, so it reconciles with the same columns.
        ParseMoney(row["Forecasted year-end (£)"]).Should().Be(
            ParseMoney(row["Total spend to date (£)"]) + ParseMoney(row["Total Forecast (£)"]));
    }

    [Fact]
    public async Task ExportSubmissions_FundingSpend_KeepsARowForAnActivityWhoseSpendIsNoLongerReportable()
    {
        var financialYear = TestFinancialYears.Current;
        var (_, q2, removedActivityId) = await SeedTwoQuarterReportWithADeletedActivityAsync(financialYear);

        AuthenticateAsAdmin();
        var response = await GetWindowExportAsync(q2);
        var fundingSpend = await ReadZipEntryAsync(response, "funding-spend.csv");
        var lines = fundingSpend.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        var header = lines[0].Split(',');
        var rows = lines.Skip(1).Select(l => l.Split(',')).ToList();

        // Still one rectangle, and the deleted activity is named rather than silently dropped.
        rows.Should().OnlyContain(cells => cells.Length == header.Length);
        var orphan = rows.Should().ContainSingle(cells => cells[Array.IndexOf(header, "Activity")].Contains("Removed activity")).Subject;
        orphan[Array.IndexOf(header, "Activity")].Should().Contain("no longer reported");
        orphan[Array.IndexOf(header, "Activity ID")].Should().Be(removedActivityId.ToString());
        ParseMoney(orphan[Array.IndexOf(header, "Q1 total actual (£)")]).Should().Be(100m);
        orphan[Array.IndexOf(header, "Q2 Forecast (£)")].Should().BeEmpty("an orphaned row carries actuals only");

        // The per-activity table and the organisation totals describe the same money. The subtotal row (BLUM-7668)
        // is a restatement of the rows above it, not another activity, so it is excluded rather than double-counted
        // — through the "Row type" column, which is the marker a real consumer has to use: the "Subtotal" label in
        // the Activity column is free text an LA could type as an activity name.
        var rowTotals = rows
            .Where(cells => cells[Array.IndexOf(header, ReportSnapshotCsv.RowTypeColumn)] == ReportSnapshotCsv.ActivityRowType)
            .Sum(cells => ParseMoney(cells[Array.IndexOf(header, "Total actual to date (£)")]));
        var summaryRow = await ReadFundingSummaryRowAsync(response, MainOrg.Name);
        rowTotals.Should().Be(ParseMoney(summaryRow["Total spend to date (£)"]));
    }

    [Fact]
    public async Task ExportSubmissions_QuarterWhoseOnlySpendIsOrphaned_StillReportsIt()
    {
        var financialYear = TestFinancialYears.Current;
        AuthenticateAsMainOrgLead();

        // Every penny of Q1 went on the one activity that is later deleted, so by the time Q2 is reported nothing
        // reportable is left to carry Q1's money. The Q1 column used to come out blank while Total spend to date
        // still counted the £250.
        var q1 = await CreateOpenQuarterCollectionAsync(1, "Only-orphan Q1", financialYear);
        var only = await CreateRnaActivityAsync(title: "Only activity");
        await CreateSubmissionAsync(q1);
        await ConfirmActivitiesAsync(q1);
        (await SaveFundingActualsAsync(q1, saveAsDraft: true, otherAmount: null, (only.Id, 250m))).EnsureSuccessStatusCode();
        (await Client.DeleteAsync($"activities/{only.Id}")).EnsureSuccessStatusCode();

        // A report with no reportable activity at all cannot be submitted, so Q2 gets its own — with nothing spent
        // against it, keeping Q1's orphaned £250 the only money in the export.
        var q2 = await CreateOpenQuarterCollectionAsync(2, "Only-orphan Q2", financialYear);
        var replacement = await CreateRnaActivityAsync(title: "Replacement activity");
        await SeedFundingAllocationAsync(MainOrg, financialYear, 10_000m);
        await CreateSubmissionAsync(q2);
        await ConfirmActivitiesAsync(q2);
        await CompleteActivityReportAsync(q2, replacement.Id);
        await CompleteFundingSpendSectionAsync(q2, perActivityActual: 0m, otherActual: 0m, perQuarterForecast: 0m, otherForecast: 0m);
        await CompleteStrategicFeedbackSectionAsync(q2);
        (await Client.PostAsync($"quarterly-report/{q2}/submission/submit", null)).EnsureSuccessStatusCode();

        AuthenticateAsAdmin();
        var response = await GetWindowExportAsync(q2);

        (await ReadZipEntryAsync(response, "funding-spend.csv")).Should().Contain("Only activity (no longer reported)");

        var row = await ReadFundingSummaryRowAsync(response, MainOrg.Name);
        ParseMoney(row["Q1 total actual (£)"]).Should().Be(250m, "blank would hide money the total still counts");
        ParseMoney(row["Q2 total actual (£)"]).Should().Be(0m, "£0 was recorded, which is not the same as nothing being recorded");
        ParseMoney(row["Total spend to date (£)"]).Should().Be(250m);
    }

    // BLUM-7672: a Planned activity's forecast reaches DHSC. It is a row like any other in funding-spend.csv — actual
    // cells blank (it was never asked for one), forecast cells filled — and its forecast is inside funding-summary.csv's
    // Q{n} Forecast and Total Forecast, which still reconcile with Forecasted year-end.
    [Fact]
    public async Task ExportSubmissions_PlannedActivityForecast_ReachesBothFundingFiles()
    {
        var financialYear = TestFinancialYears.Current;
        AuthenticateAsMainOrgLead();

        var q1 = await CreateOpenQuarterCollectionAsync(1, "Planned forecast Q1", financialYear);
        var active = await CreateRnaActivityAsync(title: "Active work");
        var planned = await CreateRnaActivityAsync(status: "planned", title: "Planned work");
        await SeedFundingAllocationAsync(MainOrg, financialYear, 10_000m);
        await CreateSubmissionAsync(q1);
        await ConfirmActivitiesAsync(q1);
        await CompleteActivityReportAsync(q1, active.Id);

        // £100 actual on the active activity + £20 other; £50 per remaining quarter on BOTH activities + £10 other.
        await CompleteFundingSpendSectionAsync(q1);
        await CompleteStrategicFeedbackSectionAsync(q1);
        (await Client.PostAsync($"quarterly-report/{q1}/submission/submit", null)).EnsureSuccessStatusCode();

        AuthenticateAsAdmin();
        var response = await GetWindowExportAsync(q1);

        var lines = (await ReadZipEntryAsync(response, "funding-spend.csv")).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        var header = lines[0].Split(',');
        string Cell(string[] cells, string column) => cells[Array.IndexOf(header, column)];
        var activityIdColumn = Array.IndexOf(header, "Activity ID");
        var plannedRow = lines.Skip(1)
            .Select(l => l.Split(','))
            .Should().ContainSingle(cells => cells[activityIdColumn] == planned.Id.ToString()).Subject;

        Cell(plannedRow, "Activity").Should().Be("Planned work");
        Cell(plannedRow, ReportSnapshotCsv.RowTypeColumn).Should().Be(ReportSnapshotCsv.ActivityRowType);
        Cell(plannedRow, "Q1 total actual (£)").Should().BeEmpty("a Planned activity is never asked for an actual");
        ParseMoney(Cell(plannedRow, "Q2 Forecast (£)")).Should().Be(50m);
        ParseMoney(Cell(plannedRow, "Q3 Forecast (£)")).Should().Be(50m);
        ParseMoney(Cell(plannedRow, "Q4 Forecast (£)")).Should().Be(50m);
        ParseMoney(Cell(plannedRow, "Projected activity spend (£)")).Should().Be(150m);

        var summary = await ReadFundingSummaryRowAsync(response, MainOrg.Name);
        ParseMoney(summary["Q1 total actual (£)"]).Should().Be(120m, "£100 on the active activity + £20 other, nothing from the Planned one");
        ParseMoney(summary["Q2 Forecast (£)"]).Should().Be(110m, "£50 active + £50 planned + £10 other");
        ParseMoney(summary["Total Forecast (£)"]).Should().Be(330m, "3 quarters x (£50 active + £50 planned + £10 other)");
        ParseMoney(summary["Forecasted year-end (£)"]).Should().Be(
            ParseMoney(summary["Total spend to date (£)"]) + ParseMoney(summary["Total Forecast (£)"]));
    }

    // The Q4 report has nothing left to forecast, but its export still shows the forecasts the Q3 report made for Q4
    // (the Q{n} Forecast columns keep an estimate after its quarter arrives). A still-Planned activity's Q4 forecast
    // must stay in that column like an Active one's — it used to vanish, because a Q4 report gave a Planned activity
    // no row to carry it (code review of BLUM-7672).
    [Fact]
    public async Task ExportSubmissions_Q4Export_KeepsAStillPlannedActivitysEarlierForecast()
    {
        var financialYear = TestFinancialYears.Current;
        AuthenticateAsMainOrgLead();

        var active = await CreateRnaActivityAsync(title: "Active work");
        var planned = await CreateRnaActivityAsync(status: "planned", title: "Planned work");
        await SeedFundingAllocationAsync(MainOrg, financialYear, 10_000m);

        // Q3 forecasts Q4: £50 on each activity + £10 other.
        var q3 = await CreateOpenQuarterCollectionAsync(3, "Planned forecast Q3", financialYear);
        await CreateSubmissionAsync(q3);
        await ConfirmActivitiesAsync(q3);
        await CompleteActivityReportAsync(q3, active.Id);
        await CompleteFundingSpendSectionAsync(q3);
        await CompleteStrategicFeedbackSectionAsync(q3);
        (await Client.PostAsync($"quarterly-report/{q3}/submission/submit", null)).EnsureSuccessStatusCode();

        // Q4 has no forecast quarters; the activity is still Planned.
        var q4 = await CreateOpenQuarterCollectionAsync(4, "Planned forecast Q4", financialYear);
        await CreateSubmissionAsync(q4);
        await ConfirmActivitiesAsync(q4);
        await CompleteActivityReportAsync(q4, active.Id);
        await CompleteFundingSpendSectionAsync(q4);
        await CompleteStrategicFeedbackSectionAsync(q4);
        (await Client.PostAsync($"quarterly-report/{q4}/submission/submit", null)).EnsureSuccessStatusCode();

        AuthenticateAsAdmin();
        var response = await GetWindowExportAsync(q4);

        var summary = await ReadFundingSummaryRowAsync(response, MainOrg.Name);
        ParseMoney(summary["Q4 Forecast (£)"]).Should().Be(110m, "Q3's £50 active + £50 planned + £10 other estimate for Q4");

        var lines = (await ReadZipEntryAsync(response, "funding-spend.csv")).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        var header = lines[0].Split(',');
        var activityIdColumn = Array.IndexOf(header, "Activity ID");
        var plannedRow = lines.Skip(1)
            .Select(l => l.Split(','))
            .Should().ContainSingle(cells => cells[activityIdColumn] == planned.Id.ToString()).Subject;
        ParseMoney(plannedRow[Array.IndexOf(header, "Q4 Forecast (£)")]).Should().Be(50m);
        plannedRow[Array.IndexOf(header, "Activity")].Should().Be("Planned work", "a live activity is not \"no longer reported\"");
    }

    // Q1 records spend against two activities plus "Other", one of those activities is then deleted, and Q2 is
    // authored and submitted against what remains. Returns (q1, q2, deletedActivityId). Leaves the caller
    // authenticated as the Main-org lead.
    private async Task<(Guid Q1, Guid Q2, Guid RemovedActivityId)> SeedTwoQuarterReportWithADeletedActivityAsync(string financialYear)
    {
        AuthenticateAsMainOrgLead();

        var q1 = await CreateOpenQuarterCollectionAsync(1, "Reconciliation Q1", financialYear);
        var kept = await CreateRnaActivityAsync(title: "Kept activity");
        var removed = await CreateRnaActivityAsync(title: "Removed activity");
        await CreateSubmissionAsync(q1);
        await ConfirmActivitiesAsync(q1);
        (await SaveFundingActualsAsync(q1, saveAsDraft: true, otherAmount: 20m, (kept.Id, 100m), (removed.Id, 100m)))
            .EnsureSuccessStatusCode();

        (await Client.DeleteAsync($"activities/{removed.Id}")).EnsureSuccessStatusCode();

        var q2 = await CreateOpenQuarterCollectionAsync(2, "Reconciliation Q2", financialYear);
        await SeedFundingAllocationAsync(MainOrg, financialYear, 10_000m);
        await CreateSubmissionAsync(q2);
        await ConfirmActivitiesAsync(q2);
        await CompleteActivityReportAsync(q2, kept.Id);
        await CompleteFundingSpendSectionAsync(q2);
        await CompleteStrategicFeedbackSectionAsync(q2);
        (await Client.PostAsync($"quarterly-report/{q2}/submission/submit", null)).EnsureSuccessStatusCode();

        return (q1, q2, removed.Id);
    }

    // funding-summary.csv's row for one organisation, keyed by column name so the assertions state the arithmetic
    // rather than pinning a whole comma-joined line. Its cells are organisation identity and money, none of which
    // carries an embedded comma, so a plain split is enough — the same assumption CountCells makes.
    private static async Task<IReadOnlyDictionary<string, string>> ReadFundingSummaryRowAsync(
        HttpResponseMessage response,
        string organisationName)
    {
        var lines = (await ReadZipEntryAsync(response, "funding-summary.csv")).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        var header = lines[0].Split(',');
        var row = lines.Skip(1).Select(l => l.Split(',')).Single(cells => cells[0] == organisationName);
        return header.Select((name, i) => (name, value: row[i])).ToDictionary(x => x.name, x => x.value);
    }

    // Money as ReportSnapshotService.Money renders it: blank for "nothing recorded", and accounting parentheses for a
    // negative, because a leading '-' would trip CsvWriter's formula-injection guard.
    private static decimal ParseMoney(string cell) => cell switch
    {
        "" => 0m,
        ['(', .. var inner, ')'] => -decimal.Parse(inner, CultureInfo.InvariantCulture),
        _ => decimal.Parse(cell, CultureInfo.InvariantCulture),
    };

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

        lines[0].Should().Be("Organisation,Region,Status,Submission date,Total reported spend (£),Financial year,Quarter");
        lines.Should().HaveCount(3); // header + the two assigned orgs

        // The submitted org: Completed, Actual spend summed (the 999 forecast is excluded), FY + quarter.
        var completedLine = lines.Single(l => l.Contains(",Completed,"));
        completedLine.Should().Contain($",150.00,{TestFinancialYears.Current},1");

        // The non-submitter: Not started, blank date, zero spend.
        var notStartedLine = lines.Single(l => l.Contains(",Not started,"));
        notStartedLine.Should().Contain($",,0.00,{TestFinancialYears.Current},1");
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
            "summary.csv", "activity-results.csv", "funding-spend.csv", "funding-summary.csv", "strategic-feedback.csv");
    }

    [Fact]
    public async Task ExportSubmissions_SectionFiles_CarrySubmitterSnapshotDataWithOrgColumns()
    {
        await factory.SetFeatureAsync(Feature.BLUM_8329, enabled: false);

        // MainOrg submits (with a frozen snapshot); SecondOrg is assigned but never submits.
        var collectionId = await CreateOpenCollectionAsync(localAuthorityIds: [MainOrg.Id.Value, SecondOrg.Id.Value]);
        await SeedSubmittedSubmissionWithSpendAsync(collectionId, MainOrg, 150m, 0m, 0m, SampleSnapshot());

        AuthenticateAsAdmin();
        var response = await GetWindowExportAsync(collectionId);

        var activity = await ReadZipEntryAsync(response, "activity-results.csv");
        activity.Split("\r\n")[0].Should().Be("Organisation,Region,ONS code,E-code,Activity,Question,Answer");
        activity.Should().Contain("Main Council,North East,E06000001,E1000001,Public Health Officer post");
        activity.Should().Contain("Recruited a public health officer");

        var funding = await ReadZipEntryAsync(response, "funding-spend.csv");
        funding.Should().Contain("Q2 Forecast (£)");
        funding.Should().Contain(
            $"Main Council,North East,E06000001,E1000001,{TestFinancialYears.Current},Public Health Officer post,Public health,{SampleActivityId},150.00,40.00,,,,,,150.00,190.00,Activity");

        // The per-organisation totals are a different grain, so they live in their own wide file rather than under
        // the per-activity money columns.
        var fundingSummary = (await ReadZipEntryAsync(response, "funding-summary.csv")).Split("\r\n");
        fundingSummary[0].Should().Be(
            "Organisation,Region,ONS code,E-code,Financial year,Annual allocation (£),Annual allocation financial year,"
            + "Next year annual allocation (£),Next year financial year,Q1 total actual (£),Q2 Forecast (£),"
            + "Q2 total actual (£),Q3 Forecast (£),Q3 total actual (£),Q4 Forecast (£),Q4 total actual (£),"
            + "Total spend to date (£),Remaining allocation (£),Total Forecast (£),Forecasted year-end (£),Forecasted variance (£)");
        fundingSummary.Should().Contain($"Main Council,North East,E06000001,E1000001,{TestFinancialYears.Current},1000.00,,,,150.00,40.00,,,,,,150.00,850.00,40.00,190.00,810.00");
        funding.Should().NotContain("Total grant allocation");

        var feedback = await ReadZipEntryAsync(response, "strategic-feedback.csv");
        feedback.Split("\r\n")[0].Should().Be("Organisation,Region,ONS code,E-code,Section,Question,Answer");
        feedback.Should().Contain("On track for the year");

        // The non-submitter is in the summary but has no rows in any section file (no snapshot to decompose).
        var summary = await ReadZipEntryAsync(response, "summary.csv");
        summary.Should().Contain(SecondOrg.Name);
        activity.Should().NotContain(SecondOrg.Name);
        feedback.Should().NotContain(SecondOrg.Name);
    }

    // BLUM-8106, driven through the real flow because the defect was in what ReportSnapshotService froze: a report
    // against a collection published without strategic feedback used to carry every blank feedback page into its
    // snapshot, and from there into this file. The window ZIP keeps its fixed set of files (other organisations can
    // have had the section), so the organisation simply contributes no rows to it — while its other sections still do.
    // The per-organisation download leaves the file out altogether, like the LA's own.
    [Fact]
    public async Task ExportSubmissions_FormLeftOffTheCollection_ContributesNoRowsToItsFile()
    {
        AuthenticateAsMainOrgLead();
        var activity = await CreateRnaActivityAsync(title: "Needs assessment without feedback");
        var collectionId = await CreateQuarterlyCollectionAsync(
            "Q1 quarterly report without strategic feedback",
            Today.AddDays(-1),
            Today.AddDays(60),
            formModuleIds: await FormModuleIdsExceptAsync(FeedbackModuleCode));

        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);
        await CompleteActivityReportAsync(collectionId, activity.Id);
        await CompleteFundingSpendSectionAsync(collectionId);
        (await SubmitReportAsync(collectionId)).EnsureSuccessStatusCode();

        AuthenticateAsAdmin();
        var response = await GetWindowExportAsync(collectionId);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        (await ReadZipEntryNamesAsync(response)).Should().BeEquivalentTo(
            "summary.csv", "activity-results.csv", "funding-spend.csv", "funding-summary.csv", "strategic-feedback.csv");
        (await ReadZipEntryAsync(response, "strategic-feedback.csv")).Split("\r\n")
            .Should().ContainSingle("the only submitter had no strategic feedback section, so the file is header-only");
        (await ReadZipEntryAsync(response, "activity-results.csv"))
            .Should().Contain("Needs assessment without feedback", "the sections the report did have still export");

        var orgExport = await GetOrgExportAsync(collectionId, MainOrg.Id.Value);
        orgExport.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadZipEntryNamesAsync(orgExport)).Should().NotContain("strategic-feedback.csv");
    }

    // ---- BLUM-8329: strategic-feedback.csv as one row per organisation -----------------------------------

    // The four real questions as a snapshot frozen before rows carried a question code holds them — by label only,
    // which is every report submitted before BLUM-8329 shipped — with BLUM_7637's " (optional)" suffix on the first
    // two when that flag was on at submit, the plain copy when it was off. Submissions in one window can carry
    // either, so each test below mixes them. (A report submitted now carries codes; ReportExportSnapshotTests covers it.)
    private static ReportSnapshotDocument SnapshotWithFeedback(
        bool optionalLabels,
        string otherPrevention,
        string challenges,
        string dhscFeedback,
        string learnings)
    {
        var suffix = optionalLabels ? " (optional)" : string.Empty;
        return SampleSnapshot() with
        {
            FeedbackSections =
            [
                new ReportSnapshotSection(
                    "Wider progress and challenges",
                    [
                        new ReportSnapshotRow("What other gambling harms prevention work have you focused on this quarter?" + suffix, otherPrevention),
                        new ReportSnapshotRow("What challenges have you encountered?" + suffix, challenges),
                    ]),
                new ReportSnapshotSection(
                    "Working together",
                    [
                        new ReportSnapshotRow("Do you have any feedback for the Department of Health and Social Care (DHSC) on gambling harms prevention?", dhscFeedback),
                        new ReportSnapshotRow("Do you have any learnings you’d like to share with other local authorities?", learnings),
                    ]),
            ],
        };
    }

    // AC2-AC5: one row per submitting local authority, the name first, then each answer under its question, then the
    // identity columns the other files in the ZIP join on. SecondOrg is seeded first, so the order proves the file is
    // sorted by name; the assigned VCSE never submitted and so has no answers to show.
    [Fact]
    public async Task ExportSubmissions_WithOneRowPerLocalAuthority_ListsEachSubmitterOnceWithAColumnPerQuestion()
    {
        await factory.SetFeatureAsync(Feature.BLUM_8329, enabled: true);
        var collectionId = await CreateOpenCollectionAsync(localAuthorityIds: [MainOrg.Id.Value, SecondOrg.Id.Value, VcseOrg.Id.Value]);
        await SeedSubmittedSubmissionWithSpendAsync(
            collectionId,
            SecondOrg,
            100m,
            0m,
            0m,
            SnapshotWithFeedback(optionalLabels: false, "Worked with the local football club", string.Empty, "Clearer funding dates", "No"));
        await SeedSubmittedSubmissionWithSpendAsync(
            collectionId,
            MainOrg,
            150m,
            0m,
            0m,
            SnapshotWithFeedback(optionalLabels: true, "Ran awareness sessions in libraries", "Recruitment delays", "More guidance on evaluation", "Yes"));

        AuthenticateAsAdmin();
        var response = await GetWindowExportAsync(collectionId);

        (await ReadZipEntryAsync(response, "strategic-feedback.csv")).Split("\r\n").Should().Equal(
            $"{WideStrategicFeedbackHeader},Region,ONS code,E-code",
            "Main Council,Ran awareness sessions in libraries,Recruitment delays,More guidance on evaluation,Yes,North East,E06000001,E1000001",
            "Second Council,Worked with the local football club,,Clearer funding dates,No,North West,E06000002,E1000002");
    }

    // The region filter narrows this file like every other: the in-region row stays, the other region's goes.
    [Fact]
    public async Task ExportSubmissions_WithOneRowPerLocalAuthority_WithRegion_KeepsOnlyThatRegionsRow()
    {
        await factory.SetFeatureAsync(Feature.BLUM_8329, enabled: true);
        var collectionId = await CreateOpenCollectionAsync(localAuthorityIds: [MainOrg.Id.Value, SecondOrg.Id.Value]);
        await SeedSubmittedSubmissionWithSpendAsync(
            collectionId,
            MainOrg,
            100m,
            0m,
            0m,
            SnapshotWithFeedback(optionalLabels: false, "Main prevention work", "Main challenges", "Main feedback", "Yes"));
        await SeedSubmittedSubmissionWithSpendAsync(
            collectionId,
            SecondOrg,
            200m,
            0m,
            0m,
            SnapshotWithFeedback(optionalLabels: false, "Second prevention work", "Second challenges", "Second feedback", "No"));

        AuthenticateAsAdmin();
        var response = await GetWindowExportAsync(collectionId, Regions.NorthEast);

        (await ReadZipEntryAsync(response, "strategic-feedback.csv")).Split("\r\n").Should().Equal(
            $"{WideStrategicFeedbackHeader},Region,ONS code,E-code",
            "Main Council,Main prevention work,Main challenges,Main feedback,Yes,North East,E06000001,E1000001");
    }

    // DHSC's download of one organisation's report is that LA's own ZIP, so its feedback file is the LA's single row.
    [Fact]
    public async Task ExportOrganisation_WithOneRowPerLocalAuthority_IsThatLocalAuthoritysSingleRow()
    {
        await factory.SetFeatureAsync(Feature.BLUM_8329, enabled: true);
        var collectionId = await CreateOpenCollectionAsync();
        await SeedSubmittedSubmissionWithSpendAsync(
            collectionId,
            MainOrg,
            100m,
            0m,
            0m,
            SnapshotWithFeedback(optionalLabels: true, "Ran awareness sessions in libraries", string.Empty, "More guidance on evaluation", "No"));

        AuthenticateAsAdmin();
        var response = await GetOrgExportAsync(collectionId, MainOrg.Id.Value);

        (await ReadZipEntryAsync(response, "strategic-feedback.csv")).Split("\r\n").Should().Equal(
            WideStrategicFeedbackHeader,
            "Main Council,Ran awareness sessions in libraries,,More guidance on evaluation,No");
    }

    // A frozen funding table with more than one row — the shape a subtotal actually has to add up (BLUM-7668).
    // Three rows, because the subtotal totals everything an organisation contributes: two activities plus the
    // "Other" row, one of which carries an accounting-negative correction. Q3's columns and Q4 Forecast are blank on
    // every row, while Q4 total actual is a recorded 0.00 on exactly one — the pair that proves blank and 0.00 stay
    // different answers all the way through a subtotal.
    //
    // One activity is deliberately NAMED "Subtotal". Activity names are free text and mandatory (BLUM-7620), so a
    // real LA can type that, and it is why the generated row is marked by the "Row type" column rather than by the
    // label in the Activity cell — a consumer filtering on the label would drop this activity's money instead.
    private static ReportSnapshotDocument MultiRowFundingSnapshot() => new(
        [],
        new ReportSnapshotFunding(
            ReportSnapshotCsv.FundingHeader,
            [
                [
                    "Alpha post", "Public health", Guid.Parse("22222222-2222-2222-2222-222222222222").ToString(),
                    "100.00", "40.00", "25.00", string.Empty, string.Empty, string.Empty, string.Empty, "125.00", "165.00"
                ],
                [
                    ReportSnapshotCsv.SubtotalLabel, "Housing", Guid.Parse("33333333-3333-3333-3333-333333333333").ToString(),
                    "0.00", "10.00", "5.50", string.Empty, string.Empty, string.Empty, "0.00", "5.50", "15.50"
                ],
                [
                    "Other (not linked to an activity)", string.Empty, string.Empty,
                    "50.00", string.Empty, "(2.50)", string.Empty, string.Empty, string.Empty, string.Empty, "47.50", "47.50"
                ]
            ],
            [new ReportSnapshotRow("Total spend to date", "178.00")]),
        []);

    [Fact]
    public async Task ExportSubmissions_FundingSpend_ClosesEachOrganisationsBlockWithASubtotalOfItsOwnRows()
    {
        // Off, because this groups every row by its Organisation cell: the BLUM-9021 grand total has none and would
        // read as a third, organisation-less block.
        await SetGrandTotalAsync(false);

        // BLUM-7668: rows are grouped into a contiguous block per organisation, and each block ends in a subtotal of
        // that organisation's OWN rows. Main Council contributes three rows, Second Council one — a single-row
        // organisation still gets a subtotal, and neither organisation's money may leak into the other's.
        var collectionId = await CreateOpenCollectionAsync(localAuthorityIds: [MainOrg.Id.Value, SecondOrg.Id.Value]);
        await SeedSubmittedSubmissionWithSpendAsync(collectionId, MainOrg, 0m, 0m, 0m, MultiRowFundingSnapshot());
        await SeedSubmittedSubmissionWithSpendAsync(collectionId, SecondOrg, 0m, 0m, 0m, SampleSnapshot());

        AuthenticateAsAdmin();
        var lines = (await ReadZipEntryAsync(await GetWindowExportAsync(collectionId), "funding-spend.csv"))
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        var header = lines[0].Split(',');
        var organisation = Array.IndexOf(header, "Organisation");
        var activity = Array.IndexOf(header, "Activity");
        var rowType = Array.IndexOf(header, ReportSnapshotCsv.RowTypeColumn);
        rowType.Should().Be(header.Length - 1, "the marker is appended, so every pre-existing column keeps its index");

        // Blocks as the file actually lays them out, so "immediately after the last activity row, before the next
        // organisation's rows begin" is asserted from position rather than assumed.
        var blocks = new List<List<string[]>>();
        foreach (var row in lines.Skip(1).Select(l => l.Split(',')))
        {
            if (blocks.Count == 0 || blocks[^1][0][organisation] != row[organisation])
            {
                blocks.Add([]);
            }

            blocks[^1].Add(row);
        }

        blocks.Select(block => block[0][organisation]).Should().Equal(
            new[] { MainOrg.Name, SecondOrg.Name }, "each organisation's rows stay one contiguous block, ordered by name");

        foreach (var block in blocks)
        {
            // Classified by the machine-readable column, never by the Activity label — MultiRowFundingSnapshot
            // contains an activity actually called "Subtotal", which the label alone cannot tell from the real one.
            var activityRows = block.Where(cells => cells[rowType] == ReportSnapshotCsv.ActivityRowType).ToList();
            activityRows.Should().NotBeEmpty();
            block.Where(cells => cells[rowType] == ReportSnapshotCsv.SubtotalRowType)
                .Should().ContainSingle("one subtotal closes the block, and nothing else in it is one");
            block[^1][rowType].Should().Be(
                ReportSnapshotCsv.SubtotalRowType, "the subtotal is the last row of the block, not a floating extra");
            block[^1][activity].Should().Be(ReportSnapshotCsv.SubtotalLabel, "a human reading the Activity column still sees it");

            var subtotal = block[^1];
            subtotal.Should().HaveSameCount(header, "the file stays one rectangle");
            subtotal[organisation].Should().Be(activityRows[0][organisation], "the subtotal is still filterable by LA");
            subtotal[Array.IndexOf(header, "Region")].Should().Be(activityRows[0][Array.IndexOf(header, "Region")]);
            subtotal[Array.IndexOf(header, "ONS code")].Should().Be(activityRows[0][Array.IndexOf(header, "ONS code")]);
            subtotal[Array.IndexOf(header, "E-code")].Should().Be(activityRows[0][Array.IndexOf(header, "E-code")]);
            subtotal[Array.IndexOf(header, "Financial year")].Should().Be(TestFinancialYears.Current);
            subtotal[Array.IndexOf(header, "Category")].Should().BeEmpty();
            subtotal[Array.IndexOf(header, "Activity ID")].Should().BeEmpty();

            for (var column = 0; column < header.Length; column++)
            {
                if (!header[column].EndsWith("(£)", StringComparison.Ordinal))
                {
                    continue;
                }

                // Read out of the file rather than hard-coded: the assertion is that the subtotal restates these
                // rows, which a literal would stop testing the moment the fixture changed.
                var recorded = activityRows.Select(cells => cells[column]).Where(cell => cell.Length > 0).ToList();
                if (recorded.Count == 0)
                {
                    subtotal[column].Should().BeEmpty(
                        "nothing was recorded under \"{0}\", and blank is not the same answer as 0.00", header[column]);
                    continue;
                }

                ParseMoney(subtotal[column]).Should().Be(
                    recorded.Sum(ParseMoney), "the subtotal of \"{0}\" is the sum of that organisation's own rows", header[column]);
            }
        }

        // The figures themselves, so a subtotal that summed the wrong rows cannot pass by agreeing with itself.
        var mainSubtotal = blocks[0][^1];
        mainSubtotal[Array.IndexOf(header, "Q1 total actual (£)")].Should().Be("150.00");
        mainSubtotal[Array.IndexOf(header, "Q2 Forecast (£)")].Should().Be("50.00");
        mainSubtotal[Array.IndexOf(header, "Q2 total actual (£)")].Should().Be("28.00", "a negative correction is subtracted, not skipped");
        mainSubtotal[Array.IndexOf(header, "Q3 total actual (£)")].Should().BeEmpty();
        mainSubtotal[Array.IndexOf(header, "Q4 total actual (£)")].Should().Be("0.00", "one row recorded zero, which is an answer");
        mainSubtotal[Array.IndexOf(header, "Total actual to date (£)")].Should().Be("178.00");
        mainSubtotal[Array.IndexOf(header, "Projected activity spend (£)")].Should().Be("228.00");
    }

    [Fact]
    public async Task ExportSubmissions_FundingSpend_RendersANegativeSubtotalWithoutTrippingTheFormulaGuard()
    {
        // The subtotal travels the same CsvWriter.Escape path as every other money cell, so it must render an
        // accounting-negative too: a leading '-' would be prefixed with a single quote and drop the organisation's
        // total out of Excel's own sum/sort. Two corrections that outweigh the one positive row make the total
        // negative without any individual cell being unusual.
        var snapshot = new ReportSnapshotDocument(
            [],
            new ReportSnapshotFunding(
                ReportSnapshotCsv.FundingHeader,
                [
                    ["Refunded post", "Public health", SampleActivityId.ToString(), "10.00", string.Empty, string.Empty,
                        string.Empty, string.Empty, string.Empty, string.Empty, "10.00", "10.00"],
                    ["Other (not linked to an activity)", string.Empty, string.Empty, "(160.00)", string.Empty, string.Empty,
                        string.Empty, string.Empty, string.Empty, string.Empty, "(160.00)", "(160.00)"]
                ],
                []),
            []);

        var collectionId = await CreateOpenCollectionAsync();
        await SeedSubmittedSubmissionWithSpendAsync(collectionId, MainOrg, 0m, 0m, 0m, snapshot);

        AuthenticateAsAdmin();
        var lines = (await ReadZipEntryAsync(await GetWindowExportAsync(collectionId), "funding-spend.csv"))
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        var header = lines[0].Split(',');
        var subtotal = lines.Skip(1).Select(l => l.Split(','))
            .Single(cells => cells[Array.IndexOf(header, ReportSnapshotCsv.RowTypeColumn)] == ReportSnapshotCsv.SubtotalRowType);

        subtotal[Array.IndexOf(header, "Q1 total actual (£)")].Should().Be("(150.00)");
        subtotal.Should().NotContain(cell => cell.StartsWith('-') || cell.StartsWith('\''));
    }

    // ---- BLUM-9021: the grand total closing the consolidated funding-spend.csv (behind Feature.BLUM_9021) ----------

    // The restart that applies a flag in a deployed environment re-runs the seed, and so does this. The flag lives on
    // this class's settings singleton and outlives the test that flipped it, so every test whose answer depends on the
    // grand total being there — or not — sets it on its first line rather than trusting the default.
    private Task SetGrandTotalAsync(bool enabled) => factory.SetFeatureAsync(Feature.BLUM_9021, enabled);

    // Parsed as CSV rather than split on commas: CsvWriter quotes any cell holding one, and real LA names do
    // ("Bristol, City of"), as do several seeded category names — a plain split would shift every later column index.
    private async Task<(string[] Header, List<string[]> Rows)> ReadFundingSpendAsync(Guid collectionId, string? region = null)
    {
        AuthenticateAsAdmin();
        var lines = (await ReadZipEntryAsync(await GetWindowExportAsync(collectionId, region), "funding-spend.csv"))
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        return (CsvParser.ParseCsvLine(lines[0]), lines.Skip(1).Select(CsvParser.ParseCsvLine).ToList());
    }

    [Fact]
    public async Task ExportSubmissions_FundingSpend_WithGrandTotalOn_ClosesTheFileWithATotalOfEveryOrganisation()
    {
        // Two organisations, one with three rows (MultiRowFundingSnapshot: a negative correction, blank Q3 columns,
        // a recorded 0.00 in Q4, and an activity NAMED "Subtotal") and one with a single row.
        await SetGrandTotalAsync(true);
        var collectionId = await CreateOpenCollectionAsync(localAuthorityIds: [MainOrg.Id.Value, SecondOrg.Id.Value]);
        await SeedSubmittedSubmissionWithSpendAsync(collectionId, MainOrg, 0m, 0m, 0m, MultiRowFundingSnapshot());
        await SeedSubmittedSubmissionWithSpendAsync(collectionId, SecondOrg, 0m, 0m, 0m, SampleSnapshot());

        var (header, rows) = await ReadFundingSpendAsync(collectionId);
        var rowType = Array.IndexOf(header, ReportSnapshotCsv.RowTypeColumn);

        rows.Where(cells => cells[rowType] == ReportSnapshotCsv.GrandTotalRowType)
            .Should().ContainSingle("one grand total closes the file, and nothing else in it is one");
        var grandTotal = rows[^1];
        grandTotal[rowType].Should().Be(
            ReportSnapshotCsv.GrandTotalRowType, "the grand total is the last row, below every organisation's subtotal");
        grandTotal.Should().HaveSameCount(header, "the file stays one rectangle");
        grandTotal[Array.IndexOf(header, "Activity")].Should().Be(ReportSnapshotCsv.GrandTotalLabel, "a human reading the Activity column sees it");
        grandTotal[Array.IndexOf(header, "Financial year")].Should().Be(TestFinancialYears.Current);
        foreach (var column in new[] { "Organisation", "Region", "ONS code", "E-code", "Category", "Activity ID" })
        {
            grandTotal[Array.IndexOf(header, column)].Should().BeEmpty(
                "the grand total belongs to no single organisation or activity, so \"{0}\" is blank", column);
        }

        // Classified by the machine-readable column: MultiRowFundingSnapshot has an activity called "Subtotal".
        var activityRows = rows.Where(cells => cells[rowType] == ReportSnapshotCsv.ActivityRowType).ToList();
        var subtotalRows = rows.Where(cells => cells[rowType] == ReportSnapshotCsv.SubtotalRowType).ToList();
        activityRows.Should().HaveCount(4);
        subtotalRows.Should().HaveCount(2, "the per-organisation subtotals are still there, one per organisation");

        for (var column = 0; column < header.Length; column++)
        {
            if (!header[column].EndsWith("(£)", StringComparison.Ordinal))
            {
                continue;
            }

            // Read out of the file rather than hard-coded, so the assertion is that the grand total restates these rows.
            var recorded = activityRows.Select(cells => cells[column]).Where(cell => cell.Length > 0).ToList();
            if (recorded.Count == 0)
            {
                grandTotal[column].Should().BeEmpty(
                    "no organisation recorded anything under \"{0}\", and blank is not the same answer as 0.00", header[column]);
                continue;
            }

            ParseMoney(grandTotal[column]).Should().Be(
                recorded.Sum(ParseMoney), "the grand total of \"{0}\" is the sum of every organisation's rows", header[column]);
            ParseMoney(grandTotal[column]).Should().Be(
                subtotalRows.Sum(cells => ParseMoney(cells[column])),
                "the grand total of \"{0}\" agrees with the subtotals above it",
                header[column]);
        }

        // The figures themselves, so a grand total that summed the wrong rows cannot pass by agreeing with itself:
        // Main Council's subtotal (see the BLUM-7668 test above) plus Second Council's single row.
        grandTotal[Array.IndexOf(header, "Q1 total actual (£)")].Should().Be("300.00");
        grandTotal[Array.IndexOf(header, "Q2 Forecast (£)")].Should().Be("90.00");
        grandTotal[Array.IndexOf(header, "Q2 total actual (£)")].Should().Be("28.00", "a negative correction is subtracted, not skipped");
        grandTotal[Array.IndexOf(header, "Q3 total actual (£)")].Should().BeEmpty();
        grandTotal[Array.IndexOf(header, "Q4 total actual (£)")].Should().Be("0.00", "one row recorded zero, which is an answer");
        grandTotal[Array.IndexOf(header, "Total actual to date (£)")].Should().Be("328.00", "actual spend across every LA and quarter");
        grandTotal[Array.IndexOf(header, "Projected activity spend (£)")].Should().Be("418.00");
    }

    [Fact]
    public async Task ExportSubmissions_FundingSpend_GrandTotalFlag_AddsOneLastRowAndChangesNothingElse()
    {
        // The same window exported with the flag off and then on. Off must be the file production downloads today —
        // every organisation's block closed by its subtotal and nothing after — and on must be exactly that file plus
        // one line, so switching the flag can never move or alter a row an analyst already relies on.
        await SetGrandTotalAsync(false);
        var collectionId = await CreateOpenCollectionAsync(localAuthorityIds: [MainOrg.Id.Value, SecondOrg.Id.Value]);
        await SeedSubmittedSubmissionWithSpendAsync(collectionId, MainOrg, 0m, 0m, 0m, MultiRowFundingSnapshot());
        await SeedSubmittedSubmissionWithSpendAsync(collectionId, SecondOrg, 0m, 0m, 0m, SampleSnapshot());

        AuthenticateAsAdmin();
        var off = await ReadZipEntryAsync(await GetWindowExportAsync(collectionId), "funding-spend.csv");

        var offRows = off.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(CsvParser.ParseCsvLine).ToList();
        offRows.Should().NotContain(
            cells => cells[cells.Length - 1] == ReportSnapshotCsv.GrandTotalRowType, "the flag is off, so production's file is unchanged");
        offRows[^1][^1].Should().Be(ReportSnapshotCsv.SubtotalRowType, "the file still ends on the last organisation's subtotal");
        offRows[^1][0].Should().Be(SecondOrg.Name);

        await SetGrandTotalAsync(true);
        AuthenticateAsAdmin();
        var on = await ReadZipEntryAsync(await GetWindowExportAsync(collectionId), "funding-spend.csv");

        on.Should().StartWith(off, "every line of the flag-off file is kept, byte for byte, in the same order");
        var added = on[off.Length..].Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        added.Should().ContainSingle("the flag adds exactly one row")
            .Which.Should().EndWith($",{ReportSnapshotCsv.GrandTotalRowType}");
    }

    [Fact]
    public async Task ExportSubmissions_FundingSpend_WithGrandTotalOnAndARegion_TotalsOnlyThatRegion()
    {
        // BLUM-7682's region filter narrows the source list once, so the grand total of a regional export is that
        // region's total — and says so in its Region cell, since the file is meant to make sense once it is off the
        // service. MainOrg is North East, SecondOrg North West.
        await SetGrandTotalAsync(true);
        var collectionId = await CreateOpenCollectionAsync(localAuthorityIds: [MainOrg.Id.Value, SecondOrg.Id.Value]);
        await SeedSubmittedSubmissionWithSpendAsync(collectionId, MainOrg, 0m, 0m, 0m, MultiRowFundingSnapshot());
        await SeedSubmittedSubmissionWithSpendAsync(collectionId, SecondOrg, 0m, 0m, 0m, SampleSnapshot());

        var (header, rows) = await ReadFundingSpendAsync(collectionId, Regions.NorthEast);
        var rowType = Array.IndexOf(header, ReportSnapshotCsv.RowTypeColumn);

        rows.Should().NotContain(cells => cells[Array.IndexOf(header, "Organisation")] == SecondOrg.Name);
        var subtotal = rows.Single(cells => cells[rowType] == ReportSnapshotCsv.SubtotalRowType);
        var grandTotal = rows[^1];
        grandTotal[rowType].Should().Be(ReportSnapshotCsv.GrandTotalRowType);
        grandTotal[Array.IndexOf(header, "Region")].Should().Be(Regions.NorthEast);
        grandTotal[Array.IndexOf(header, "Organisation")].Should().BeEmpty();

        var firstMoneyColumn = Array.IndexOf(header, "Q1 total actual (£)");
        grandTotal[firstMoneyColumn..rowType].Should().Equal(
            subtotal[firstMoneyColumn..rowType], "with one organisation in the region, its subtotal is the whole total");
        grandTotal[Array.IndexOf(header, "Total actual to date (£)")].Should().Be("178.00", "Second Council's £150 is out of region");
    }

    [Fact]
    public async Task ExportSubmissions_FundingSpend_WithGrandTotalOn_LeavesOutASubmitterWhoseFrozenTableIsAnOlderShape()
    {
        // A window can straddle a deploy that changed the funding table's columns, and a source whose frozen header
        // is not the current one is omitted from this file rather than misaligned into it. The grand total is taken
        // over the same filtered list, so money the file does not show can never appear in its total. The legacy row
        // is wide enough that its £999 sits at the index of today's "Q1 total actual (£)" — totalled over the
        // unfiltered list, it would land there.
        await SetGrandTotalAsync(true);
        var legacy = new ReportSnapshotDocument(
            [],
            new ReportSnapshotFunding(
                ["Activity", "Category", "Activity ID", "Levy spent this quarter (£)", "Total spent so far (£)"],
                [["Legacy post", "Public health", SampleActivityId.ToString(), "999.00", "999.00"]],
                []),
            []);
        var collectionId = await CreateOpenCollectionAsync(localAuthorityIds: [MainOrg.Id.Value, SecondOrg.Id.Value]);
        await SeedSubmittedSubmissionWithSpendAsync(collectionId, MainOrg, 0m, 0m, 0m, MultiRowFundingSnapshot());
        await SeedSubmittedSubmissionWithSpendAsync(collectionId, SecondOrg, 0m, 0m, 0m, legacy);

        var (header, rows) = await ReadFundingSpendAsync(collectionId);
        var rowType = Array.IndexOf(header, ReportSnapshotCsv.RowTypeColumn);

        rows.Should().NotContain(cells => cells[Array.IndexOf(header, "Organisation")] == SecondOrg.Name);
        var subtotal = rows.Single(cells => cells[rowType] == ReportSnapshotCsv.SubtotalRowType);
        var grandTotal = rows[^1];
        grandTotal[rowType].Should().Be(ReportSnapshotCsv.GrandTotalRowType);

        var firstMoneyColumn = Array.IndexOf(header, "Q1 total actual (£)");
        grandTotal[firstMoneyColumn..rowType].Should().Equal(
            subtotal[firstMoneyColumn..rowType], "the only organisation in the file is the whole of its total");
        grandTotal[firstMoneyColumn].Should().Be("150.00", "the legacy organisation's £999 is not in the file");
    }

    [Fact]
    public async Task ExportSubmissions_FundingSpend_WithGrandTotalOnAndNoSubmitters_IsStillHeaderOnly()
    {
        // Nothing to total is not a total of nothing: an all-blank grand total would be a data row in a file that has
        // always been header-only for an empty window (and an empty region, BLUM-7682).
        await SetGrandTotalAsync(true);
        var collectionId = await CreateOpenCollectionAsync();

        var (_, rows) = await ReadFundingSpendAsync(collectionId);

        rows.Should().BeEmpty();
    }

    [Fact]
    public async Task ExportSubmissions_NoSubmitters_SectionFilesAreHeaderOnly()
    {
        // Assigned org that never submits: the section files are valid but carry only their headers.
        var collectionId = await CreateOpenCollectionAsync();

        AuthenticateAsAdmin();
        var response = await GetWindowExportAsync(collectionId);

        response.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");

        var activity = (await ReadZipEntryAsync(response, "activity-results.csv")).Split("\r\n");
        activity.Should().ContainSingle().Which.Should().Be("Organisation,Region,ONS code,E-code,Activity,Question,Answer");

        var funding = (await ReadZipEntryAsync(response, "funding-spend.csv")).Split("\r\n");
        funding.Should().ContainSingle()
            .Which.Should().Be(
                "Organisation,Region,ONS code,E-code,Financial year,Activity,Category,Activity ID,Q1 total actual (£),"
                + "Q2 Forecast (£),Q2 total actual (£),Q3 Forecast (£),Q3 total actual (£),Q4 Forecast (£),"
                + "Q4 total actual (£),Total actual to date (£),Projected activity spend (£),Row type");

        var fundingSummary = (await ReadZipEntryAsync(response, "funding-summary.csv")).Split("\r\n");
        fundingSummary.Should().ContainSingle()
            .Which.Should().Be(
                "Organisation,Region,ONS code,E-code,Financial year,Annual allocation (£),Annual allocation financial year,"
                + "Next year annual allocation (£),Next year financial year,Q1 total actual (£),Q2 Forecast (£),"
                + "Q2 total actual (£),Q3 Forecast (£),Q3 total actual (£),Q4 Forecast (£),Q4 total actual (£),"
                + "Total spend to date (£),Remaining allocation (£),Total Forecast (£),Forecasted year-end (£),Forecasted variance (£)");
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

        foreach (var name in new[] { "summary.csv", "activity-results.csv", "funding-spend.csv", "funding-summary.csv", "strategic-feedback.csv" })
        {
            var lines = (await ReadZipEntryAsync(response, name)).Split("\r\n");
            var width = CountCells(lines[0]);
            lines.Should().OnlyContain(line => CountCells(line) == width, $"every row of {name} must match its header width");
        }

        // Both organisations' metrics are present, each under the summary file's own columns.
        var fundingSummary = await ReadZipEntryAsync(response, "funding-summary.csv");
        fundingSummary.Should().Contain($"Main Council,North East,E06000001,E1000001,{TestFinancialYears.Current},1000.00,,,,150.00");
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
        (await ReadZipEntryAsync(response, "activity-results.csv")).Split("\r\n")
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
        (await ReadZipEntryAsync(response, "activity-results.csv")).Split("\r\n")
            .Should().ContainSingle("a snapshot with no usable rows contributes only the header");

        // The same document must survive the per-organisation export, which deserialises it through the other path.
        (await GetOrgExportAsync(collectionId, MainOrg.Id.Value)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ExportSubmissions_OrgWithNoFundingSection_ContributesBlankTotalsRowRatherThanBeingOmitted()
    {
        // A valid, fully-decoded snapshot whose Funding is null (no funding section recorded) must still contribute
        // one row to funding-summary.csv — the wide file stays a single rectangle across every organisation in the
        // export (BLUM-7130 CSV review), the same way it already did for the melted Summary shape.
        var noFundingSnapshot = new ReportSnapshotDocument(
            [new ReportSnapshotSection("Public Health Officer post", [new ReportSnapshotRow("Q", "A")])],
            null,
            []);

        var collectionId = await CreateOpenCollectionAsync();
        await SeedSubmittedSubmissionWithSpendAsync(collectionId, MainOrg, 0m, 0m, 0m, noFundingSnapshot);

        AuthenticateAsAdmin();
        var response = await GetWindowExportAsync(collectionId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var blankTotals = string.Join(",", Enumerable.Repeat(string.Empty, 16));
        (await ReadZipEntryAsync(response, "funding-summary.csv")).Split("\r\n")
            .Should().Contain($"Main Council,North East,E06000001,E1000001,{TestFinancialYears.Current},{blankTotals}");

        // No funding section means no per-activity rows either — the file stays header-only for this org.
        (await ReadZipEntryAsync(response, "funding-spend.csv")).Split("\r\n")
            .Should().ContainSingle("no funding section means no per-activity rows");
    }

    [Fact]
    public async Task ExportSubmissions_SnapshotWithoutTotals_ExportsBlankTotalsCellsRatherThanThrowing()
    {
        // A snapshot frozen before ReportSnapshotFundingTotals existed has no "totals" in its stored JSON at all.
        // Constructing ReportSnapshotFunding with the parameter omitted round-trips to the same null Totals a truly
        // legacy document would deserialise to (System.Text.Json treats an absent property and an explicit null the
        // same way via the record's default parameter). The wide funding-summary.csv row must render blank cells
        // for this organisation rather than throwing or omitting the row.
        var legacySnapshot = new ReportSnapshotDocument(
            [],
            new ReportSnapshotFunding(
                ["Activity", "Levy spent this quarter (£)", "Total spent so far (£)"],
                [["Public Health Officer post", "150.00", "150.00"]],
                [new ReportSnapshotRow("Total grant allocation", "1000.00")]),
            []);

        var collectionId = await CreateOpenCollectionAsync();
        await SeedSubmittedSubmissionWithSpendAsync(collectionId, MainOrg, 150m, 0m, 0m, legacySnapshot);

        AuthenticateAsAdmin();
        var response = await GetWindowExportAsync(collectionId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var blankTotals = string.Join(",", Enumerable.Repeat(string.Empty, 16));
        (await ReadZipEntryAsync(response, "funding-summary.csv")).Split("\r\n")
            .Should().Contain($"Main Council,North East,E06000001,E1000001,{TestFinancialYears.Current},{blankTotals}");
    }

    [Fact]
    public async Task ExportSubmissions_MaturityAssessmentCollection_IsTheMaturityExport()
    {
        // The same URL serves the maturity window's consolidated export (BLUM-7859 AC4), audited under its own action;
        // MaturityAssessmentDhscTests covers the file's content.
        var collectionId = await CreateMaturityAssessmentCollectionAsync(localAuthorityIds: [MainOrg.Id.Value]);

        AuthenticateAsAdmin();
        var response = await GetWindowExportAsync(collectionId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadZipEntryNamesAsync(response)).Should().BeEquivalentTo(["summary.csv", "maturity-assessment.csv"]);

        var actions = await Context.AuditLogs.AsNoTracking().Select(a => a.Action).ToListAsync();
        actions.Should().Contain(AuditAction.MaturityAssessmentExported);
        actions.Should().NotContain(AuditAction.SubmissionExported);
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
        // An all-regions export records a null region rather than omitting the key, so the trail says which of the two
        // shapes was downloaded rather than leaving it to be inferred from the absence of a field.
        entries[0].After.Should().Contain("\"region\": null");
    }

    // ---- BLUM-7682: the consolidated export filtered to one region ----------------------------------

    // AC2. The trap this pins: `reportable` feeds summary.csv, but the four section files are built from a SEPARATE
    // orgById lookup over the submitters. Filtering only the former passes a summary-row assertion and still ships an
    // export named after one region containing every region's answers, with nothing thrown.
    [Fact]
    public async Task ExportSubmissions_WithRegion_IncludesOnlyThatRegionsOrganisationsInEveryFile()
    {
        // Every file here leads with {Organisation, Region}; BLUM_8329's strategic-feedback.csv puts the answers
        // between them, and ExportSubmissions_WithOneRowPerLocalAuthority_WithRegion_KeepsOnlyThatRegionsRow covers it.
        await factory.SetFeatureAsync(Feature.BLUM_8329, enabled: false);

        // Off, because every data row is asserted to start with the organisation's name; the BLUM-9021 grand total
        // belongs to no organisation. The grand total's own regional behaviour is pinned separately below.
        await SetGrandTotalAsync(false);

        // MainOrg is North East, SecondOrg is North West — both submitters with a frozen snapshot, so both would
        // otherwise contribute rows to every file in the ZIP.
        var collectionId = await CreateOpenCollectionAsync(localAuthorityIds: [MainOrg.Id.Value, SecondOrg.Id.Value]);
        await SeedSubmittedSubmissionWithSpendAsync(collectionId, MainOrg, 100m, 50m, 0m, SampleSnapshot());
        await SeedSubmittedSubmissionWithSpendAsync(collectionId, SecondOrg, 200m, 25m, 0m, SampleSnapshot());

        AuthenticateAsAdmin();
        var response = await GetWindowExportAsync(collectionId, Regions.NorthEast);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var summary = (await ReadZipEntryAsync(response, "summary.csv")).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        summary.Should().HaveCount(2); // header + MainOrg only
        summary[1].Should().StartWith($"{MainOrg.Name},{Regions.NorthEast},");

        // The out-of-region organisation must not appear in ANY entry — summary or section file.
        (await ReadWholeZipTextAsync(response)).Should().NotContain(SecondOrg.Name);

        // ...and the in-region submitter's snapshot rows are still there, so the filter narrowed rather than emptied.
        foreach (var entryName in new[] { "activity-results.csv", "funding-spend.csv", "funding-summary.csv", "strategic-feedback.csv" })
        {
            var lines = (await ReadZipEntryAsync(response, entryName)).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            lines.Should().HaveCountGreaterThan(1, "{0} should still carry the in-region submitter's rows", entryName);
            lines.Skip(1).Should().OnlyContain(l => l.StartsWith($"{MainOrg.Name},{Regions.NorthEast},", StringComparison.Ordinal));
        }
    }

    // The de-assigned-but-submitted path resolves its organisation through a second lookup inside
    // ResolveReportableOrganisations. It reads the same filtered list, and this is what proves it.
    [Fact]
    public async Task ExportSubmissions_WithRegion_ExcludesAnOutOfRegionOrgThatSubmittedButIsNoLongerAssigned()
    {
        // Assigned to MainOrg (North East) only; SecondOrg (North West) submitted and was then de-assigned.
        var collectionId = await CreateOpenCollectionAsync(localAuthorityIds: [MainOrg.Id.Value]);
        await SeedSubmittedSubmissionWithSpendAsync(collectionId, SecondOrg, 100m, 50m, 0m, SampleSnapshot());

        AuthenticateAsAdmin();

        // Unfiltered, it is included — that is the BLUM-6100 review behaviour this must not regress.
        (await ReadWholeZipTextAsync(await GetWindowExportAsync(collectionId))).Should().Contain(SecondOrg.Name);

        var northEast = await GetWindowExportAsync(collectionId, Regions.NorthEast);
        (await ReadWholeZipTextAsync(northEast)).Should().NotContain(SecondOrg.Name);

        // And it IS reachable through its own region, so it is filtered rather than dropped.
        var northWest = await GetWindowExportAsync(collectionId, Regions.NorthWest);
        (await ReadWholeZipTextAsync(northWest)).Should().Contain(SecondOrg.Name);
    }

    // A VCSE has a region exactly as an LA does, so a regional export of an explicitly-assigned collection includes it.
    // (An all-LAs collection still excludes VCSEs — that rule is upstream of the region filter and unchanged.)
    [Fact]
    public async Task ExportSubmissions_WithRegion_IncludesAVcseInThatRegion()
    {
        var collectionId = await CreateOpenCollectionAsync(localAuthorityIds: [MainOrg.Id.Value, VcseOrg.Id.Value]);

        AuthenticateAsAdmin();
        var summary = await ReadZipEntryAsync(await GetWindowExportAsync(collectionId, Regions.London), "summary.csv");

        summary.Should().Contain(VcseOrg.Name);
        summary.Should().NotContain(MainOrg.Name);
    }

    // AC1's other half: the saved file has to say what it holds once it is off the service.
    [Theory]
    [InlineData(Regions.NorthEast, "north-east")]
    [InlineData(Regions.YorkshireAndTheHumber, "yorkshire-and-the-humber")]
    public async Task ExportSubmissions_WithRegion_NamesTheRegionInTheFileName(string region, string expectedSegment)
    {
        var collectionId = await CreateOpenCollectionAsync(localAuthorityIds: [MainOrg.Id.Value]);

        AuthenticateAsAdmin();
        var response = await GetWindowExportAsync(collectionId, region);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var disposition = response.Content.Headers.ContentDisposition;
        disposition.Should().NotBeNull("the download must name the file it is serving");
        var fileName = (disposition!.FileNameStar ?? disposition.FileName ?? string.Empty).Trim('"');
        fileName.Should().Be($"submissions-{TestFinancialYears.Current.Replace('/', '-')}-q1-{expectedSegment}.zip");
    }

    // The unfiltered download must be untouched by the filter existing — same name, same organisations.
    [Fact]
    public async Task ExportSubmissions_WithoutARegion_KeepsTheAllOrganisationsFileNameAndContents()
    {
        var collectionId = await CreateOpenCollectionAsync(localAuthorityIds: [MainOrg.Id.Value, SecondOrg.Id.Value]);

        AuthenticateAsAdmin();
        var response = await GetWindowExportAsync(collectionId);

        var disposition = response.Content.Headers.ContentDisposition;
        var fileName = (disposition!.FileNameStar ?? disposition.FileName ?? string.Empty).Trim('"');
        fileName.Should().Be($"submissions-{TestFinancialYears.Current.Replace('/', '-')}-q1.zip");

        var summary = await ReadZipEntryAsync(response, "summary.csv");
        summary.Should().Contain(MainOrg.Name);
        summary.Should().Contain(SecondOrg.Name);
    }

    // "?region=" is what an empty select posts. It means every organisation, not an unsupported region — the controller
    // normalises it to null before the validator sees it, so this must be a 200 and not a 400.
    [Fact]
    public async Task ExportSubmissions_WithAnEmptyRegionParameter_ExportsEveryOrganisation()
    {
        var collectionId = await CreateOpenCollectionAsync(localAuthorityIds: [MainOrg.Id.Value, SecondOrg.Id.Value]);

        AuthenticateAsAdmin();
        var response = await GetWindowExportAsync(collectionId, string.Empty);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var summary = await ReadZipEntryAsync(response, "summary.csv");
        summary.Should().Contain(MainOrg.Name);
        summary.Should().Contain(SecondOrg.Name);
    }

    // A region with nobody in it is an empty answer, not an error — the same shape a window with no submitters already
    // produces. All five entries are present so a consumer's parser does not have to special-case it.
    [Fact]
    public async Task ExportSubmissions_WithARegionThatHasNoOrganisations_ReturnsAHeaderOnlyZip()
    {
        var collectionId = await CreateOpenCollectionAsync(localAuthorityIds: [MainOrg.Id.Value, SecondOrg.Id.Value]);
        await SeedSubmittedSubmissionWithSpendAsync(collectionId, MainOrg, 100m, 50m, 0m, SampleSnapshot());

        AuthenticateAsAdmin();
        var response = await GetWindowExportAsync(collectionId, Regions.SouthWest);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var entries = await ReadZipEntryNamesAsync(response);
        entries.Should().BeEquivalentTo(
            "summary.csv", "activity-results.csv", "funding-spend.csv", "funding-summary.csv", "strategic-feedback.csv");

        foreach (var entryName in entries)
        {
            var lines = (await ReadZipEntryAsync(response, entryName)).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            lines.Should().HaveCount(1, "{0} should be a header with no data rows", entryName);
        }
    }

    // An unsupported region cannot come from the client's select, so it is the standard rejected-input envelope rather
    // than a bespoke error. Region.IsSupported is Ordinal, so "london" is rejected alongside "Atlantis".
    [Theory]
    [InlineData("Atlantis")]
    [InlineData("london")]
    public async Task ExportSubmissions_WithAnUnsupportedRegion_Returns400_AndIsNotAudited(string region)
    {
        var collectionId = await CreateOpenCollectionAsync(localAuthorityIds: [MainOrg.Id.Value]);

        AuthenticateAsAdmin();
        var response = await GetWindowExportAsync(collectionId, region);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("validation_failed");
        // The C# property name, which client/src/views/Admin/Organisations/errors.ts keys its fieldNameMap on.
        body.Should().Contain("\"property\":\"Region\"");
        body.Should().Contain("supported English regions");

        // Validation runs in the MediatR pipeline, before the handler, so a rejected export writes no audit row.
        (await Context.AuditLogs.AsNoTracking().Where(a => a.Action == AuditAction.SubmissionExported).ToListAsync())
            .Should().BeEmpty();
    }

    // The audit trail has to say which slice was taken, and organisationCount has to describe the file that was
    // actually built rather than the window.
    [Fact]
    public async Task ExportSubmissions_WithRegion_AuditsTheRegionAndTheInRegionCount()
    {
        var collectionId = await CreateOpenCollectionAsync(localAuthorityIds: [MainOrg.Id.Value, SecondOrg.Id.Value]);

        AuthenticateAsAdmin();
        (await GetWindowExportAsync(collectionId, Regions.NorthEast)).StatusCode.Should().Be(HttpStatusCode.OK);

        var entries = await Context.AuditLogs.AsNoTracking()
            .Where(a => a.Action == AuditAction.SubmissionExported)
            .ToListAsync();

        entries.Should().ContainSingle();
        // jsonb, so Postgres reorders keys and re-spaces — match each key/value pair, not the serialised object.
        entries[0].After.Should().Contain($"\"region\": \"{Regions.NorthEast}\"");
        entries[0].After.Should().Contain("\"organisationCount\": 1");
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

    // BLUM-7647: the admin's per-organisation download runs through the same builder as the LA's own, so the file it
    // saves names the organisation and the period (AC1/AC2) and every row inside leads with the organisation (AC3).
    // Pinned on this route as well because it is the one the ticket was raised against, and the two callers reach the
    // builder by different paths (Permission vs an explicit organisation id).
    [Fact]
    public async Task ExportOrgReport_NamesTheOrganisationInTheFileNameAndOnEveryRow()
    {
        var collectionId = await CreateOpenQuarterCollectionAsync(2, "Q2 per-org export");
        await SeedSubmittedSubmissionWithSpendAsync(collectionId, MainOrg, 100m, 0m, 0m, SampleSnapshot());

        AuthenticateAsAdmin();
        var response = await GetOrgExportAsync(collectionId, MainOrg.Id.Value);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var disposition = response.Content.Headers.ContentDisposition;
        disposition.Should().NotBeNull("the download must name the file it is serving");
        var fileName = (disposition!.FileNameStar ?? disposition.FileName ?? string.Empty).Trim('"');
        fileName.Should().Be($"Main-Council-Q2-{TestFinancialYears.Current.Replace('/', '-')}.zip");

        foreach (var entryName in new[] { "report-summary.csv", "activity-results.csv", "strategic-feedback.csv" })
        {
            var lines = (await ReadZipEntryAsync(response, entryName)).Split("\r\n", StringSplitOptions.None);

            lines[0].Should().StartWith("Local authority,", "{0}'s header names the identity column first", entryName);
            foreach (var line in lines.Skip(1).Where(l => l.Length > 0))
            {
                line.Should().StartWith($"{MainOrg.Name},", "every row of {0} carries the organisation name", entryName);
            }
        }
    }

    // A submission finalised before report snapshots existed exports an empty document, so two of its four files have
    // nothing to say. Those entries used to be bare prose with no organisation on them — the sparsest download, the
    // one with least else to identify it by, was the single case a reader could not attribute (BLUM-7647 review).
    [Fact]
    public async Task ExportOrgReport_LegacySubmissionWithNoSnapshot_StillNamesTheOrganisationInEveryFile()
    {
        var collectionId = await CreateOpenCollectionAsync();
        await SeedSubmittedSubmissionWithSpendAsync(collectionId, MainOrg, 100m, 0m, 0m);

        AuthenticateAsAdmin();
        var response = await GetOrgExportAsync(collectionId, MainOrg.Id.Value);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        foreach (var (entryName, notice) in new[]
        {
            ("funding-spend.csv", "No funding spend data was recorded."),
            ("strategic-feedback.csv", "No strategic feedback was recorded."),
        })
        {
            var lines = (await ReadZipEntryAsync(response, entryName)).Split("\r\n", StringSplitOptions.None);

            lines[0].Should().Be("Local authority,Note", "{0} keeps the identity column even with nothing to report", entryName);
            lines[1].Should().Be($"{MainOrg.Name},{notice}");
        }
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