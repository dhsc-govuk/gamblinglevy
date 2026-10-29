using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using Api.Dtos;
using Application.Common.Features;
using Application.Common.ReportSnapshots;
using Domain.Collections;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Tests.Common;
using Xunit;

namespace Api.Tests.Integration.QuarterlyReports;

// Report finalisation hardening (BLUM-6613 review fixes): reference-number uniqueness across collections that share a
// financial year + quarter, and the "immutable copy for your records" export — frozen at submit, formula-injection
// safe, UK-local submission time, and excluding conditionally-hidden answers.
public class ReportExportSnapshotTests(IntegrationTestWebFactory factory) : QuarterlyReportTestBase(factory)
{
    private static async Task<string> ReadZipEntryAsync(HttpResponseMessage response, string entryName)
    {
        response.EnsureSuccessStatusCode();

        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var entry = archive.GetEntry(entryName);
        entry.Should().NotBeNull("the export ZIP should contain {0}", entryName);

        using var reader = new StreamReader(entry!.Open());
        return await reader.ReadToEndAsync();
    }

    private Task<HttpResponseMessage> SubmitReportAsync(Guid collectionId) =>
        Client.PostAsync($"quarterly-report/{collectionId}/submission/submit", null);

    private async Task CompleteAllSectionsAsync(Guid collectionId, Guid activityId)
    {
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);
        await CompleteActivityReportAsync(collectionId, activityId);
        await CompleteFundingSpendSectionAsync(collectionId);
        await CompleteStrategicFeedbackSectionAsync(collectionId);
    }

    private async Task<string> ReadCsvFromExportAsync(Guid collectionId, string entryName) =>
        await ReadZipEntryAsync(await Client.GetAsync($"quarterly-report/{collectionId}/export"), entryName);

    private async Task<IReadOnlyList<string>> ReadExportEntryNamesAsync(Guid collectionId)
    {
        var response = await Client.GetAsync($"quarterly-report/{collectionId}/export");
        response.EnsureSuccessStatusCode();

        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        return archive.Entries.Select(e => e.Name).ToList();
    }

    // Finding 2: two quarterly collections sharing a financial year + quarter previously both minted sequence 1 and
    // collided on the global reference-number index (HTTP 500). The sequence is now scoped to the FY+quarter.
    [Fact]
    public async Task Submit_TwoCollectionsInSameFinancialYearAndQuarter_GenerateDistinctReferences()
    {
        AuthenticateAsMainOrgLead();
        var activity = await CreateRnaActivityAsync();
        var collectionA = await CreateOpenQuarterCollectionAsync(1, "2026 Q1 collection A");
        var collectionB = await CreateOpenQuarterCollectionAsync(1, "2026 Q1 collection B");

        await CompleteAllSectionsAsync(collectionA, activity.Id);
        var first = await (await SubmitReportAsync(collectionA)).ToResponseModel<SubmitReportResultDto>();

        await CompleteAllSectionsAsync(collectionB, activity.Id);
        var secondResponse = await SubmitReportAsync(collectionB);

        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var second = await secondResponse.ToResponseModel<SubmitReportResultDto>();

        first.ReferenceNumber.Should().Be(TestFinancialYears.Reference(TestFinancialYears.Current, 1, 1));
        second.ReferenceNumber.Should().Be(TestFinancialYears.Reference(TestFinancialYears.Current, 1, 2));
    }

    // Finding 4: the export must serve the report frozen at submit time, not re-read live activity state.
    [Fact]
    public async Task Export_FreezesActivityTitleCapturedAtSubmit()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await CreateOpenCollectionAsync();
        var activity = await CreateRnaActivityAsync(title: "Original activity title");
        await CompleteAllSectionsAsync(collectionId, activity.Id);
        (await SubmitReportAsync(collectionId)).EnsureSuccessStatusCode();

        var before = await ReadCsvFromExportAsync(collectionId, "activity-results.csv");
        before.Should().Contain("Original activity title");

        // A live-data change after submission must NOT alter the finalised report.
        await RenameActivityAsync(activity, "Renamed after submission");

        var after = await ReadCsvFromExportAsync(collectionId, "activity-results.csv");
        after.Should().Be(before);
        after.Should().NotContain("Renamed after submission");
    }

    // Finding 1: a user answer beginning with a formula trigger must not become a live spreadsheet formula. Both
    // shapes of strategic-feedback.csv (BLUM_8329), since the wide one builds its rows separately.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Export_NeutralisesFormulaInjectionInAnswers(bool oneRowPerLocalAuthority)
    {
        await factory.SetFeatureAsync(Feature.BLUM_8329, oneRowPerLocalAuthority);
        AuthenticateAsMainOrgLead();
        var collectionId = await CreateOpenCollectionAsync();
        var activity = await CreateRnaActivityAsync();
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);
        await CompleteActivityReportAsync(collectionId, activity.Id);
        await CompleteFundingSpendSectionAsync(collectionId);
        await CompleteStrategicFeedbackWithAsync(collectionId, "=SUM(A1:A9)");
        (await SubmitReportAsync(collectionId)).EnsureSuccessStatusCode();

        var csv = await ReadCsvFromExportAsync(collectionId, "strategic-feedback.csv");

        csv.Should().Contain("'=SUM(A1:A9)", "a leading '=' is neutralised with a single quote");
        csv.Should().NotContain(",=SUM", "no cell may begin with a bare formula trigger");
    }

    // BLUM-8329: behind BLUM_8329 the LA's strategic-feedback.csv is their one row, each answer under its question,
    // and DHSC's window export gives the same organisation the same answers. Driven through the real seeded questions
    // in both BLUM_7637 states, because that flag changes two labels: a snapshot frozen now places its answers by the
    // question codes it carries, so neither state may move one.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Export_WithOneRowPerLocalAuthority_PutsEachAnswerUnderItsQuestion(bool widerProgressOptional)
    {
        await factory.SetFeatureAsync(Feature.BLUM_8329, enabled: true);
        await factory.SetFeatureAsync(Feature.BLUM_7637, widerProgressOptional);
        AuthenticateAsMainOrgLead();
        var collectionId = await CreateOpenCollectionAsync();
        var activity = await CreateRnaActivityAsync();
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);
        await CompleteActivityReportAsync(collectionId, activity.Id);
        await CompleteFundingSpendSectionAsync(collectionId);
        (await SaveStrategicFeedbackAsync(
            collectionId,
            saveAsDraft: true,
            ("SF_OTHER_PREVENTION", "Ran awareness sessions in libraries"),
            ("SF_CHALLENGES", "Recruitment delays"),
            ("SF_DHSC_SUPPORT", "More guidance on evaluation"),
            ("SF_LEARNINGS", "yes"))).EnsureSuccessStatusCode();
        (await CompleteStrategicFeedbackAsync(collectionId)).EnsureSuccessStatusCode();
        (await SubmitReportAsync(collectionId)).EnsureSuccessStatusCode();

        // The codes are frozen with the rows, so the columns never depend on the labels those rows happen to carry.
        var submission = await Context.Submissions
            .AsNoTracking()
            .FirstAsync(s => s.CollectionId == new CollectionId(collectionId) && s.OrganisationId == MainOrg.Id);
        ReportSnapshotJson.Deserialize(submission.ReportSnapshotJson!)!.FeedbackSections
            .SelectMany(section => section.Rows)
            .Select(row => row.Code)
            .Should().Equal("SF_OTHER_PREVENTION", "SF_CHALLENGES", "SF_DHSC_SUPPORT", "SF_LEARNINGS");

        const string answers = "Ran awareness sessions in libraries,Recruitment delays,More guidance on evaluation,Yes";

        var local = SplitRows(await ReadCsvFromExportAsync(collectionId, "strategic-feedback.csv"));
        local.Should().Equal(WideStrategicFeedbackHeader, $"{MainOrg.Name},{answers}");

        AuthenticateAsAdmin();
        var window = SplitRows(await ReadZipEntryAsync(
            await Client.GetAsync($"data-collections/{collectionId}/submissions/export"),
            "strategic-feedback.csv"));
        window.Should().Equal(
            $"{WideStrategicFeedbackHeader},Region,ONS code,E-code",
            $"{MainOrg.Name},{answers},{MainOrg.Region},{MainOrg.OnsCode},{MainOrg.ECode}");
    }

    // BLUM-7216 follow-up: the submit endpoint builds the report snapshot BEFORE the command re-checks eligibility, so
    // an organisation de-assigned from the collection mid-report reached ReportSnapshotService with no funding section
    // to render. Folding that None through Option.Match with a null arm threw ResultIsNullException — a 500 where the
    // command's own refusal should have been returned.
    [Fact]
    public async Task Submit_AfterOrganisationDeassigned_RefusesInsteadOfFailing()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await CreateOpenCollectionAsync(localAuthorityIds: [MainOrg.Id.Value]);
        var activity = await CreateRnaActivityAsync();
        await CompleteAllSectionsAsync(collectionId, activity.Id);

        // The admin removes this LA from the window after it has authored the whole report but before it submits.
        var assignment = await Context.CollectionLocalAuthorities
            .FirstAsync(x => x.CollectionId == new CollectionId(collectionId) && x.LocalAuthorityId == MainOrg.Id);
        Context.CollectionLocalAuthorities.Remove(assignment);
        await SaveChangesAsync();

        AuthenticateAsMainOrgLead();
        var response = await SubmitReportAsync(collectionId);

        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
        response.IsSuccessStatusCode.Should().BeFalse("a de-assigned organisation must not be able to submit");

        // And nothing was finalised behind the refusal.
        var submission = await Context.Submissions
            .AsNoTracking()
            .FirstAsync(s => s.CollectionId == new CollectionId(collectionId) && s.OrganisationId == MainOrg.Id);
        submission.SubmittedAt.Should().BeNull();
    }

    // Finding 6: the submitted-at time is shown in UK local time, not raw UTC.
    [Fact]
    public async Task Export_SummaryShowsSubmittedAtInUkLocalTime()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await CreateOpenCollectionAsync();
        var activity = await CreateRnaActivityAsync();
        await CompleteAllSectionsAsync(collectionId, activity.Id);
        (await SubmitReportAsync(collectionId)).EnsureSuccessStatusCode();

        var csv = await ReadCsvFromExportAsync(collectionId, "report-summary.csv");

        csv.Should().Contain("(UK time)");
    }

    // BLUM-7216 follow-up: every collection setup field is nullable (a quarterly collection can be saved as a partial
    // draft, and a published one can be reverted to draft and cleared), and the summary CSV used to null-forgive five
    // of them. A submitted report against such a collection took the whole download down with a 500 rather than
    // reporting the gap. The columns are set through raw SQL because the domain entity has no way to unset them.
    [Fact]
    public async Task Export_CollectionMissingSetupFields_StillDownloadsAndReportsTheGaps()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await CreateOpenCollectionAsync();
        var activity = await CreateRnaActivityAsync();
        await CompleteAllSectionsAsync(collectionId, activity.Id);
        (await SubmitReportAsync(collectionId)).EnsureSuccessStatusCode();

        await Context.Database.ExecuteSqlRawAsync(
            """
            update collections
            set quarter = null,
                financial_year = null,
                reporting_period_start = null,
                reporting_period_end = null,
                submission_deadline = null
            where id = {0}
            """,
            collectionId);

        var csv = await ReadCsvFromExportAsync(collectionId, "report-summary.csv");

        // The report itself is unaffected — only the fields that are genuinely absent say so.
        csv.Should().Contain("Reference number");
        csv.Should().Contain("(UK time)");
        csv.Should().Contain("Not recorded");
    }

    // Finding 5: conditionally-hidden questions (hidden on screen because their parent answer does not match) must not
    // leak into the export.
    [Fact]
    public async Task Export_OmitsConditionallyHiddenQuestions()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await CreateOpenCollectionAsync();
        var activity = await CreateRnaActivityAsync();
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);

        var questions = await GetActivityQuestionsAsync(collectionId, activity.Id);

        // A conditional child whose parent is a required, unconditional choice question we can answer to hide it.
        var child = questions.Questions.First(q =>
            !string.IsNullOrEmpty(q.ConditionalQuestionCode)
            && !string.IsNullOrEmpty(q.ConditionalValue)
            && questions.Questions.Any(p =>
                p.Code == q.ConditionalQuestionCode
                && string.IsNullOrEmpty(p.ConditionalQuestionCode)
                && p.Options.Count > 0));
        var parent = questions.Questions.First(q => q.Code == child.ConditionalQuestionCode);
        var allowed = child.ConditionalValue!.Split(',').Select(v => v.Trim()).ToHashSet(StringComparer.Ordinal);
        var hidingValue = parent.Options.First(o => !allowed.Contains(o.Value)).Value;

        var answers = questions.Questions
            .Where(q => q.IsRequired && string.IsNullOrEmpty(q.ConditionalQuestionCode))
            .Select(q => (q.Code, (string?)(q.Code == parent.Code ? hidingValue : PlausibleAnswer(q))))
            .ToArray();
        (await SaveAnswersAsync(collectionId, activity.Id, saveAsDraft: true, answers)).EnsureSuccessStatusCode();
        (await CompleteActivityAsync(collectionId, activity.Id)).EnsureSuccessStatusCode();

        await CompleteFundingSpendSectionAsync(collectionId);
        await CompleteStrategicFeedbackSectionAsync(collectionId);
        (await SubmitReportAsync(collectionId)).EnsureSuccessStatusCode();

        var csv = await ReadCsvFromExportAsync(collectionId, "activity-results.csv");

        csv.Should().Contain(parent.Label, "the visible parent question must appear");
        csv.Should().NotContain(child.Label, "the conditionally-hidden child question must be excluded");
    }

    // BLUM-8106: a form left off the collection gives the LA no section for it, so it must not reach their copy of the
    // report either — neither as a file in the ZIP nor in the snapshot frozen at submit, which is what the DHSC
    // cross-organisation export reads. Every module used to be rendered regardless: activity results arrived
    // pre-filled from the activity record, funding with a row per activity, and every strategic-feedback page blank.
    [Theory]
    [InlineData(KeyResultsModuleCode, "activity-results.csv")]
    [InlineData(FundingModuleCode, "funding-spend.csv")]
    [InlineData(FeedbackModuleCode, "strategic-feedback.csv")]
    public async Task Export_LeavesOutTheSectionOfAFormTheCollectionDidNotInclude(string excludedModule, string excludedEntry)
    {
        AuthenticateAsMainOrgLead();
        var activity = await CreateRnaActivityAsync();
        var collectionId = await CreateQuarterlyCollectionAsync(
            $"Q1 quarterly report without {excludedModule}",
            Today.AddDays(-1),
            Today.AddDays(60),
            formModuleIds: await FormModuleIdsExceptAsync(excludedModule));

        var taskList = await CreateSubmissionAsync(collectionId);
        taskList.Sections.Should().NotContain(s => s.ModuleCode == excludedModule, "the LA is never asked for this form");
        await ConfirmActivitiesAsync(collectionId);

        if (excludedModule != KeyResultsModuleCode)
        {
            await CompleteActivityReportAsync(collectionId, activity.Id);
        }

        if (excludedModule != FundingModuleCode)
        {
            await CompleteFundingSpendSectionAsync(collectionId);
        }

        if (excludedModule != FeedbackModuleCode)
        {
            await CompleteStrategicFeedbackSectionAsync(collectionId);
        }

        (await SubmitReportAsync(collectionId)).EnsureSuccessStatusCode();

        string[] everyEntry = ["report-summary.csv", "activity-results.csv", "funding-spend.csv", "strategic-feedback.csv"];
        (await ReadExportEntryNamesAsync(collectionId)).Should().BeEquivalentTo(
            everyEntry.Where(e => e != excludedEntry),
            "only {0} belongs to the form the collection left out",
            excludedEntry);

        var submission = await Context.Submissions
            .AsNoTracking()
            .FirstAsync(s => s.CollectionId == new CollectionId(collectionId) && s.OrganisationId == MainOrg.Id);
        var snapshot = ReportSnapshotJson.Deserialize(submission.ReportSnapshotJson!);
        snapshot.Should().NotBeNull();

        // Both directions per part, so the test also fails if the fix drops a section the report DID have.
        (snapshot!.ActivitySections.Count > 0).Should().Be(
            excludedModule != KeyResultsModuleCode, "activity results are frozen only when the report has that section");
        (snapshot.Funding is not null).Should().Be(
            excludedModule != FundingModuleCode, "funding is frozen only when the report has that section");
        (snapshot.FeedbackSections.Count > 0).Should().Be(
            excludedModule != FeedbackModuleCode, "strategic feedback is frozen only when the report has that section");
    }

    // BLUM-7647 AC3: every row of every CSV in the ZIP leads with the organisation's name, so a file that has been
    // moved, renamed or mailed on still says whose answers it holds. The header row of each file leads with the
    // "Local authority" column name, and funding-spend.csv's mid-file summary sub-header takes it too or the summary
    // amounts underneath sit under the wrong columns.
    [Fact]
    public async Task Export_EveryCsvLeadsWithTheLocalAuthorityName()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await CreateOpenCollectionAsync();
        var activity = await CreateRnaActivityAsync();
        await CompleteAllSectionsAsync(collectionId, activity.Id);
        (await SubmitReportAsync(collectionId)).EnsureSuccessStatusCode();

        foreach (var entryName in new[] { "report-summary.csv", "activity-results.csv", "funding-spend.csv", "strategic-feedback.csv" })
        {
            var lines = SplitRows(await ReadCsvFromExportAsync(collectionId, entryName));

            lines.Should().NotBeEmpty("{0} should have content", entryName);
            lines[0].Should().StartWith("Local authority,", "{0}'s header names the identity column first", entryName);

            // Two rows inside funding-spend.csv are not data: the blank spacer, which has nothing to identify, and
            // the summary sub-header after it, which is a header and so leads with the column name like the first row.
            foreach (var line in lines.Skip(1).Where(l => l.Length > 0 && !l.StartsWith("Local authority,", StringComparison.Ordinal)))
            {
                line.Should().StartWith($"{MainOrg.Name},", "every row of {0} carries the organisation name", entryName);
            }
        }
    }

    // BLUM-7668: the LA's own funding-spend.csv gets the same per-organisation subtotal from the shared row builder.
    // One organisation, so one subtotal — and it closes the per-activity table, above the blank spacer and the
    // summary sub-header that this file (unlike the cross-organisation one) keeps underneath.
    [Fact]
    public async Task Export_FundingSpend_ClosesThePerActivityTableWithASubtotal()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await CreateOpenCollectionAsync();
        var activity = await CreateRnaActivityAsync();
        await CompleteAllSectionsAsync(collectionId, activity.Id);
        (await SubmitReportAsync(collectionId)).EnsureSuccessStatusCode();

        var lines = SplitRows(await ReadCsvFromExportAsync(collectionId, "funding-spend.csv"));
        var header = lines[0].Split(',');
        var activityColumn = Array.IndexOf(header, "Activity");
        var rowType = Array.IndexOf(header, ReportSnapshotCsv.RowTypeColumn);
        var totalToDate = Array.IndexOf(header, "Total actual to date (£)");
        rowType.Should().Be(header.Length - 1, "the marker is appended, so every pre-existing column keeps its index");

        // The blank spacer separates the two grains, so everything above it is the per-activity table.
        var spacer = Array.FindIndex(lines, line => line.Length == 0);
        spacer.Should().BeGreaterThan(1, "the per-activity table has rows of its own before the summary block");

        var table = lines[1..spacer].Select(line => line.Split(',')).ToList();
        table.Where(cells => cells[rowType] == ReportSnapshotCsv.SubtotalRowType)
            .Should().ContainSingle("one organisation means exactly one subtotal");
        table[^1][rowType].Should().Be(ReportSnapshotCsv.SubtotalRowType, "the subtotal closes the table");
        table[^1][activityColumn].Should().Be(ReportSnapshotCsv.SubtotalLabel, "a human reading the Activity column still sees it");
        table[..^1].Should().OnlyContain(cells => cells[rowType] == ReportSnapshotCsv.ActivityRowType);
        table[^1][0].Should().Be(MainOrg.Name, "every row of the file still names the local authority");

        // It restates the rows above it rather than stating a figure of its own.
        var recorded = table[..^1]
            .Select(cells => cells[totalToDate])
            .Where(cell => cell.Length > 0)
            .Select(cell => decimal.Parse(cell, CultureInfo.InvariantCulture))
            .ToList();
        recorded.Should().NotBeEmpty("the fixture records spend, so there is something to total");
        decimal.Parse(table[^1][totalToDate], CultureInfo.InvariantCulture).Should().Be(recorded.Sum());
    }

    // BLUM-7669/BLUM-7670: the funding summary metrics read "Forecasted", not "Projected", and "Remaining
    // allocation", not "Remaining budget". These labels are frozen INTO the snapshot at submit time
    // (ReportSnapshotService.BuildFunding) rather than resolved at export time like funding-summary.csv's column
    // headers, so nothing here fails if a rename is reverted in that one file — the export keeps working and just
    // says the old word. This is the only thing that notices.
    [Fact]
    public async Task Export_FundingSpend_SummaryBlockUsesAgreedWording()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await CreateOpenCollectionAsync();
        var activity = await CreateRnaActivityAsync();
        await CompleteAllSectionsAsync(collectionId, activity.Id);
        (await SubmitReportAsync(collectionId)).EnsureSuccessStatusCode();

        var csv = await ReadCsvFromExportAsync(collectionId, "funding-spend.csv");
        var lines = SplitRows(csv);

        // The summary block is everything below the blank spacer and its own sub-header; its label sits in the
        // "Summary" column, one past the local-authority identity cell.
        var subHeader = Array.FindIndex(lines, line => line.StartsWith($"Local authority,{ReportSnapshotCsv.FundingSummaryColumns[0]},", StringComparison.Ordinal));
        subHeader.Should().BeGreaterThan(0, "the LA's file keeps the summary metrics under their own sub-header");

        var labels = lines[(subHeader + 1)..]
            .Where(line => line.Length > 0)
            .Select(line => line.Split(',')[1])
            .ToList();

        labels.Should().Contain("Forecasted year-end");
        labels.Should().Contain("Forecasted variance");
        labels.Should().Contain("Remaining allocation");
        csv.Should().NotContain("Projected year-end");
        csv.Should().NotContain("Projected variance");
        csv.Should().NotContain("Remaining budget");
    }

    // AC1/AC2: the saved filename identifies the report before anyone opens it. The client takes the name from
    // Content-Disposition (downloadFile.ts), falling back to a generic one when the header says nothing.
    [Fact]
    public async Task Export_FileNameCarriesTheLocalAuthorityQuarterAndFinancialYear()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await CreateOpenQuarterCollectionAsync(2, "Q2 quarterly report");
        var activity = await CreateRnaActivityAsync();
        await CompleteAllSectionsAsync(collectionId, activity.Id);
        (await SubmitReportAsync(collectionId)).EnsureSuccessStatusCode();

        var response = await Client.GetAsync($"quarterly-report/{collectionId}/export");
        response.EnsureSuccessStatusCode();

        var expected = $"Main-Council-Q2-{TestFinancialYears.Current.Replace('/', '-')}.zip";
        FileNameOf(response).Should().Be(expected);
    }

    // The apostrophe sits inside a word, so it is dropped rather than collapsed to a hyphen; "&" and spaces between
    // words are separators and become one; an accented letter folds to its base letter rather than being treated as
    // punctuation ("Ynys Môn" must not download as "Ynys-M-n.zip"). Real organisation names contain all of these.
    [Theory]
    [InlineData("King's Lynn & West Norfolk", "Kings-Lynn-West-Norfolk")]
    [InlineData("Ynys Môn", "Ynys-Mon")]
    public async Task Export_FileName_FoldsAccentsDropsApostrophesAndCollapsesTheRestToHyphens(string organisationName, string expectedSlug)
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await CreateOpenQuarterCollectionAsync(3, $"Q3 quarterly report for {expectedSlug}");
        var activity = await CreateRnaActivityAsync();
        await CompleteAllSectionsAsync(collectionId, activity.Id);
        (await SubmitReportAsync(collectionId)).EnsureSuccessStatusCode();

        // Restored in the finally: the class shares one database, and a leaked rename would move the expected
        // filename under every other test here.
        await RenameOrganisationAsync(MainOrg.Id.Value, organisationName);
        try
        {
            var response = await Client.GetAsync($"quarterly-report/{collectionId}/export");
            response.EnsureSuccessStatusCode();

            var expected = $"{expectedSlug}-Q3-{TestFinancialYears.Current.Replace('/', '-')}.zip";
            FileNameOf(response).Should().Be(expected);
        }
        finally
        {
            await RenameOrganisationAsync(MainOrg.Id.Value, MainOrg.Name);
        }
    }

    // Every collection setup field is nullable (see Export_CollectionMissingSetupFields_StillDownloadsAndReportsTheGaps).
    // A missing quarter or financial year drops just that segment of the filename — a download must never fail, and
    // never render "Not recorded" into a filename either.
    [Fact]
    public async Task Export_FileName_OmitsTheSegmentsTheCollectionDoesNotHave()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await CreateOpenCollectionAsync();
        var activity = await CreateRnaActivityAsync();
        await CompleteAllSectionsAsync(collectionId, activity.Id);
        (await SubmitReportAsync(collectionId)).EnsureSuccessStatusCode();

        await Context.Database.ExecuteSqlRawAsync(
            "update collections set quarter = null, financial_year = null where id = {0}",
            collectionId);

        var response = await Client.GetAsync($"quarterly-report/{collectionId}/export");
        response.EnsureSuccessStatusCode();

        FileNameOf(response).Should().Be("Main-Council.zip");
    }

    // MVC writes both filename and filename* (FileContentResult.SetHttpFileName); either is what the browser reads,
    // and only the plain one is guaranteed unquoted-free, so both are normalised here.
    private static string FileNameOf(HttpResponseMessage response)
    {
        var disposition = response.Content.Headers.ContentDisposition;
        disposition.Should().NotBeNull("the download must name the file it is serving");
        return (disposition!.FileNameStar ?? disposition.FileName ?? string.Empty).Trim('"');
    }

    private async Task RenameOrganisationAsync(Guid organisationId, string name)
    {
        await Context.Database.ExecuteSqlRawAsync(
            "update organisations set name = {0} where id = {1}",
            name,
            organisationId);
    }

    private static string[] SplitRows(string csv) =>
        csv.Split("\r\n", StringSplitOptions.None);

    private static string PlausibleAnswer(ReportQuestionDto question) =>
        question.Options.Count > 0 ? question.Options[0].Value
        : question.QuestionType == "Number" ? "5"
        : "Progress made this quarter";

    private async Task CompleteStrategicFeedbackWithAsync(Guid collectionId, string value)
    {
        var feedback = await GetStrategicFeedbackAsync(collectionId);
        var answers = feedback.Pages
            .SelectMany(p => p.Questions)
            .Select(q => (q.Code, (string?)(q.Options.Count > 0 ? q.Options[0].Value : value)))
            .ToArray();

        (await SaveStrategicFeedbackAsync(collectionId, saveAsDraft: true, answers)).EnsureSuccessStatusCode();
        (await CompleteStrategicFeedbackAsync(collectionId)).EnsureSuccessStatusCode();
    }

    private async Task RenameActivityAsync(ActivityDto activity, string newTitle)
    {
        var request = new UpdateActivityRequestDto(
            newTitle,
            activity.CategoryId,
            activity.TaskId,
            activity.CustomTaskText,
            activity.Status,
            activity.StartMonth,
            activity.StartYear,
            SaveAsDraft: false,
            activity.ExpectedResults.Select(r => new ExpectedResultInputDto(r.KeyResultCode, r.Text)).ToList(),
            activity.Answers.Select(a => new ActivityAnswerInputDto(a.QuestionCode, a.Value)).ToList());

        var response = await Client.PutAsJsonAsync($"activities/{activity.Id}", request);
        response.IsSuccessStatusCode.Should().BeTrue(
            "renaming the activity failed: {0}", await response.Content.ReadAsStringAsync());
    }
}