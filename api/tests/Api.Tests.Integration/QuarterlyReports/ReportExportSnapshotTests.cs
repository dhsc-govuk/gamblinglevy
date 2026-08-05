using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using Api.Dtos;
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

    private async Task<string> ReadCsvFromExportAsync(Guid collectionId, string entryName)
    {
        var response = await Client.GetAsync($"quarterly-report/{collectionId}/export");
        response.EnsureSuccessStatusCode();

        var bytes = await response.Content.ReadAsByteArrayAsync();
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var entry = archive.GetEntry(entryName);
        entry.Should().NotBeNull("the export ZIP should contain {0}", entryName);

        using var reader = new StreamReader(entry!.Open());
        return await reader.ReadToEndAsync();
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

        first.ReferenceNumber.Should().Be("202701-0001");
        second.ReferenceNumber.Should().Be("202701-0002");
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

        var before = await ReadCsvFromExportAsync(collectionId, "activity-key-results.csv");
        before.Should().Contain("Original activity title");

        // A live-data change after submission must NOT alter the finalised report.
        await RenameActivityAsync(activity, "Renamed after submission");

        var after = await ReadCsvFromExportAsync(collectionId, "activity-key-results.csv");
        after.Should().Be(before);
        after.Should().NotContain("Renamed after submission");
    }

    // Finding 1: a user answer beginning with a formula trigger must not become a live spreadsheet formula.
    [Fact]
    public async Task Export_NeutralisesFormulaInjectionInAnswers()
    {
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

        var csv = await ReadCsvFromExportAsync(collectionId, "activity-key-results.csv");

        csv.Should().Contain(parent.Label, "the visible parent question must appear");
        csv.Should().NotContain(child.Label, "the conditionally-hidden child question must be excluded");
    }

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