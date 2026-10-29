using System.Net;
using System.Text;
using Api.Dtos;
using Application.Common.Csv;
using Application.Common.Features;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Application.Submissions.Exceptions;
using Domain.AuditLogs;
using Domain.Submissions;
using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tests.Common;
using Xunit;

namespace Api.Tests.Integration.QuarterlyReports;

// "Complete your report offline" (BLUM-8310, behind BLUM_8310): a CSV template per section of the LA's quarterly
// report, and one upload that takes any of them back. A valid file saves and completes its section; an invalid one
// saves nothing and comes back with the errors written into it. The file model itself (columns, helper text, cell
// reading, annotation) is unit-tested in Application.Tests.Unit/Submissions/Offline; this class covers the wiring:
// the gate, the flag, what a real seeded report's templates contain and what an upload persists.
//
// Every test switches the flag on (or off) on its first line: it lives on the class's settings singleton, which
// outlives the test.
public class QuarterlyReportOfflineTests(IntegrationTestWebFactory factory) : QuarterlyReportTestBase(factory)
{
    private const string ActivityResults = "activity-results";
    private const string FundingSpent = "funding-spent";
    private const string StrategicFeedback = "strategic-feedback";

    private sealed record ErrorEnvelope(string Error, string Message);

    private sealed record UploadErrorRow(int Row, string? Column, string? Question, string Message);

    private sealed record AnnotatedFile(string FileName, string ContentBase64);

    private sealed record UploadInvalidEnvelope(
        string Error,
        string Message,
        string ModuleCode,
        int ErrorCount,
        List<UploadErrorRow> Errors,
        AnnotatedFile AnnotatedFile);

    private Task EnableAsync(bool enabled = true) => factory.SetFeatureAsync(Feature.BLUM_8310, enabled);

    // The caller authenticates as the Main-org lead first: an identity set inside an async helper does not flow back.
    private async Task<Guid> StartConfirmedReportAsync()
    {
        var collectionId = await CreateOpenCollectionAsync();
        await CreateRnaActivityAsync();
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);
        return collectionId;
    }

    private async Task<byte[]> DownloadAsync(Guid collectionId, string section)
    {
        var response = await Client.GetAsync($"quarterly-report/{collectionId}/offline/{section}/template");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/csv");
        return await response.Content.ReadAsByteArrayAsync();
    }

    private Task<HttpResponseMessage> UploadAsync(Guid collectionId, byte[] content, string fileName = "report.csv")
    {
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/csv");
        return Client.PostAsync($"quarterly-report/{collectionId}/offline/upload", new MultipartFormDataContent { { file, "file", fileName } });
    }

    private static List<string[]> Records(byte[] file) =>
        CsvRecordReader.Read(CsvTextDecoder.Decode(file)).Select(record => record.Cells.ToArray()).ToList();

    private static int Column(List<string[]> records, string key) => Array.IndexOf(records[2], key);

    // The index of the column asking `code` in the row's own wording: the key is the code, or the code and a variant.
    private static int ColumnFor(List<string[]> records, string[] row, string code) =>
        Enumerable.Range(0, records[2].Length).Single(index =>
            (records[2][index] == code || records[2][index].StartsWith(code + "@", StringComparison.Ordinal)) && row[index] != "N/A");

    private static byte[] Save(List<string[]> records, Encoding? encoding = null)
    {
        if (encoding is null)
        {
            return CsvWriter.Build(records[0], records.Skip(1).Select(cells => (IReadOnlyList<string>)cells), byteOrderMark: true);
        }

        var text = Encoding.UTF8.GetString(CsvWriter.Build(records[0], records.Skip(1).Select(cells => (IReadOnlyList<string>)cells)));
        return encoding.GetBytes(text);
    }

    [Fact]
    public async Task ShouldAnswerEveryOfflineRouteNotFound_WhenTheFlagIsOff()
    {
        await EnableAsync(false);
        AuthenticateAsMainOrgLead();
        var collectionId = await StartConfirmedReportAsync();

        foreach (var section in new[] { ActivityResults, FundingSpent, StrategicFeedback })
        {
            var template = await Client.GetAsync($"quarterly-report/{collectionId}/offline/{section}/template");
            template.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await template.Content.ReadAsStringAsync()).Should().BeEmpty();
        }

        var upload = await UploadAsync(collectionId, Encoding.UTF8.GetBytes("a,b"));
        upload.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await upload.Content.ReadAsStringAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task ActivityResultsTemplate_ShouldHaveARowPerActivity_WithTheWebFormsValuesAndHelperText()
    {
        await EnableAsync();
        AuthenticateAsMainOrgLead();
        var collectionId = await CreateOpenCollectionAsync();
        var active = await CreateRnaActivityAsync(title: "Active assessment");
        var finished = await CreateRnaActivityAsync(status: "finished", title: "Finished assessment");
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);
        (await SaveAnswersAsync(collectionId, finished.Id, saveAsDraft: true, ("RNA_PUBLISHED", "yes"))).EnsureSuccessStatusCode();
        var web = await GetActivityQuestionsAsync(collectionId, active.Id);

        var file = await DownloadAsync(collectionId, ActivityResults);
        var records = Records(file);

        file.Take(3).Should().Equal(0xEF, 0xBB, 0xBF);
        records[2][0].Should().StartWith($"offline-template|v1|activity_key_results|{collectionId:N}|{MainOrg.Id.Value:N}|");
        records[1][0].Should().StartWith("How to answer");

        var activeRow = records.Single(cells => cells[0] == active.Id.ToString());
        var finishedRow = records.Single(cells => cells[0] == finished.Id.ToString());
        activeRow[Column(records, "activity")].Should().Be("Active assessment");
        activeRow[Column(records, "status")].Should().Be("Active");
        finishedRow[Column(records, "status")].Should().Be("Finished");

        var contributors = Column(records, "RNA_CONTRIBUTORS@active");
        records[0][contributors].Should().MatchRegex(@"^Q\d+\. Who is contributing evidence\?$");
        records[1][contributors].Should().Contain("If you choose ‘Other’, also answer");
        finishedRow[contributors].Should().Be("N/A");
        var prefill = web.Answers.Single(value => value.QuestionCode == "RNA_CONTRIBUTORS").Value;
        var labels = web.Questions.Single(question => question.Code == "RNA_CONTRIBUTORS").Options
            .Where(option => prefill!.Split(',').Contains(option.Value))
            .Select(option => option.Label);
        activeRow[contributors].Should().Be(string.Join("; ", labels), "the contributors are prefilled from the activity record, as on the web");

        var published = Column(records, "RNA_PUBLISHED@closed_finished");
        finishedRow[published].Should().Be("Yes", "a saved draft answer is in the template");
        activeRow[published].Should().Be("N/A");
        records[1][published].Should().Contain("If you answer ‘Yes’, answer").And.Contain("If you answer ‘No’, leave");
    }

    [Fact]
    public async Task FundingTemplate_ShouldHaveAnOtherRow_AndPlannedActivitiesOnlyOnTheForecast()
    {
        await EnableAsync();
        AuthenticateAsMainOrgLead();
        var collectionId = await CreateOpenCollectionAsync();
        var active = await CreateRnaActivityAsync(title: "Active work");
        var planned = await CreateRnaActivityAsync(status: "planned", title: "Planned work");
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);

        var records = Records(await DownloadAsync(collectionId, FundingSpent));

        records[0].Where(heading => heading.StartsWith('Q')).Should().Equal(
            "Q1. Amount spent this quarter (£)",
            "Q2. Expected spend in Quarter 2 (£)",
            "Q3. Expected spend in Quarter 3 (£)",
            "Q4. Expected spend in Quarter 4 (£)");
        records.Single(cells => cells[0] == planned.Id.ToString())[Column(records, "actual")].Should().Be("N/A");
        records.Single(cells => cells[0] == active.Id.ToString())[Column(records, "actual")].Should().BeEmpty();
        records[^1][0].Should().Be("OTHER");
    }

    [Fact]
    public async Task FundingTemplate_InQuarterFour_ShouldHaveNoForecast_AndLeavePlannedActivitiesOut()
    {
        await EnableAsync();
        AuthenticateAsMainOrgLead();
        var collectionId = await CreateOpenQuarterCollectionAsync(4, "Q4 quarterly report");
        var active = await CreateRnaActivityAsync(title: "Active work");
        await CreateRnaActivityAsync(status: "planned", title: "Planned work");
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);

        var records = Records(await DownloadAsync(collectionId, FundingSpent));

        records[2].Should().NotContain(key => key.StartsWith("forecast_"));
        records.Skip(3).Select(cells => cells[0]).Should().Equal(active.Id.ToString(), "OTHER");
    }

    [Fact]
    public async Task ShouldCompleteActivityResults_FromAFilledTemplate()
    {
        await EnableAsync();
        AuthenticateAsMainOrgLead();
        var collectionId = await CreateOpenCollectionAsync();
        var first = await CreateRnaActivityAsync(title: "First");
        var second = await CreateRnaActivityAsync(status: "finished", title: "Second");
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);

        var records = Records(await DownloadAsync(collectionId, ActivityResults));
        foreach (var activity in new[] { first, second })
        {
            var row = records.Single(cells => cells[0] == activity.Id.ToString());
            var web = await GetActivityQuestionsAsync(collectionId, activity.Id);
            var given = new Dictionary<string, string>();
            foreach (var question in web.Questions.OrderBy(q => q.DisplayOrder))
            {
                var visible = string.IsNullOrWhiteSpace(question.ConditionalQuestionCode)
                    || (given.TryGetValue(question.ConditionalQuestionCode, out var parent) && parent == question.ConditionalValue);
                if (!question.IsRequired || !visible)
                {
                    continue;
                }

                var option = question.Options.FirstOrDefault(o => o.Value == "yes") ?? question.Options.FirstOrDefault(o => o.Value != "other");
                given[question.Code] = option?.Value ?? "text";
                row[ColumnFor(records, row, question.Code)] = option?.Label ?? (question.QuestionType == "Number" ? "5" : $"Progress on {activity.Title}");
            }
        }

        var response = await UploadAsync(collectionId, Save(records));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var result = await response.ToResponseModel<OfflineUploadResultDto>();
        result.ModuleCode.Should().Be(KeyResultsModuleCode);
        result.TaskList.Sections.Single(s => s.ModuleCode == KeyResultsModuleCode).Status.Should().Be("completed");

        var reports = await Context.SubmissionActivityReports.AsNoTracking().ToListAsync();
        reports.Should().HaveCount(2).And.OnlyContain(report => report.Status.IsCompleted);
        (await GetActivityQuestionsAsync(collectionId, first.Id)).Answers
            .Should().Contain(value => value.QuestionCode == "AKR_DELIVERY_PROGRESS" && value.Value == "Progress on First");
        (await GetActivityQuestionsAsync(collectionId, second.Id)).Answers
            .Should().Contain(value => value.QuestionCode == "RNA_PUBLISHED" && value.Value == "yes");
    }

    [Fact]
    public async Task ShouldCompleteFundingSpent_FromAFilledTemplate()
    {
        await EnableAsync();
        AuthenticateAsMainOrgLead();
        var collectionId = await CreateOpenCollectionAsync();
        var active = await CreateRnaActivityAsync(title: "Active work");
        var planned = await CreateRnaActivityAsync(status: "planned", title: "Planned work");
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);

        var records = Records(await DownloadAsync(collectionId, FundingSpent));
        foreach (var row in records.Skip(3))
        {
            for (var index = 0; index < row.Length; index++)
            {
                if (records[2][index] == "actual" && row[index] != "N/A")
                {
                    row[index] = row[0] == "OTHER" ? "£20" : "1,500";
                }
                else if (records[2][index].StartsWith("forecast_", StringComparison.Ordinal))
                {
                    row[index] = "50";
                }
            }
        }

        var response = await UploadAsync(collectionId, Save(records));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await response.ToResponseModel<OfflineUploadResultDto>()).TaskList.Sections
            .Single(s => s.ModuleCode == FundingModuleCode).Status.Should().Be("completed");
        var funding = await GetFundingSpendAsync(collectionId);
        funding.Activities.Single(a => a.ActivityId == active.Id).ActualThisQuarter.Should().Be(1500m);
        funding.Activities.Single(a => a.ActivityId == planned.Id).Forecast.Values.Should().OnlyContain(amount => amount == 50m);
        funding.OtherActualThisQuarter.Should().Be(20m);
        funding.OtherForecast.Values.Should().OnlyContain(amount => amount == 50m);
    }

    [Fact]
    public async Task ShouldCompleteStrategicFeedback_FromAFilledTemplate_SavedAsWindows1252()
    {
        await EnableAsync();
        AuthenticateAsMainOrgLead();
        var collectionId = await StartConfirmedReportAsync();

        var records = Records(await DownloadAsync(collectionId, StrategicFeedback));
        records.Should().HaveCount(4, "strategic feedback is a single answer row");
        records[3][Column(records, "SF_OTHER_PREVENTION")] = "Spent £500 on the council’s campaign";
        records[3][Column(records, "SF_CHALLENGES")] = "Recruitment\nand budgets";
        records[3][Column(records, "SF_DHSC_SUPPORT")] = "More guidance";
        records[3][Column(records, "SF_LEARNINGS")] = "No";

        // Excel's plain "CSV (Comma delimited)" on a UK machine.
        var response = await UploadAsync(collectionId, Save(records, CodePagesEncodingProvider.Instance.GetEncoding(1252)));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var feedback = await GetStrategicFeedbackAsync(collectionId);
        feedback.SectionStatus.Should().Be("completed");
        feedback.Answers.Should().Contain(answer => answer.QuestionCode == "SF_OTHER_PREVENTION" && answer.Value == "Spent £500 on the council’s campaign");
        feedback.Answers.Should().Contain(answer => answer.QuestionCode == "SF_CHALLENGES" && answer.Value == "Recruitment\nand budgets");
        feedback.Answers.Should().Contain(answer => answer.QuestionCode == "SF_LEARNINGS" && answer.Value == "no");
    }

    [Fact]
    public async Task ShouldRoundTripAnAnswerThatLooksLikeAFormula()
    {
        await EnableAsync();
        AuthenticateAsMainOrgLead();
        var collectionId = await StartConfirmedReportAsync();
        (await SaveStrategicFeedbackAsync(
            collectionId, saveAsDraft: true, ("SF_OTHER_PREVENTION", "=1+1"), ("SF_CHALLENGES", "- a bullet"), ("SF_DHSC_SUPPORT", "None"), ("SF_LEARNINGS", "yes")))
            .EnsureSuccessStatusCode();

        var template = await DownloadAsync(collectionId, StrategicFeedback);
        Records(template)[3][Column(Records(template), "SF_OTHER_PREVENTION")].Should().Be("'=1+1", "the template neutralises formulas");

        var response = await UploadAsync(collectionId, template);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var feedback = await GetStrategicFeedbackAsync(collectionId);
        feedback.Answers.Should().Contain(answer => answer.QuestionCode == "SF_OTHER_PREVENTION" && answer.Value == "=1+1");
        feedback.Answers.Should().Contain(answer => answer.QuestionCode == "SF_CHALLENGES" && answer.Value == "- a bullet");
    }

    [Fact]
    public async Task ShouldSaveNothing_AndReturnTheFileWithItsErrors_WhenAnswersAreInvalid()
    {
        await EnableAsync();
        AuthenticateAsMainOrgLead();
        var collectionId = await StartConfirmedReportAsync();
        (await SaveStrategicFeedbackAsync(collectionId, saveAsDraft: true, ("SF_DHSC_SUPPORT", "Earlier answer"))).EnsureSuccessStatusCode();
        var records = Records(await DownloadAsync(collectionId, StrategicFeedback));
        records[3][Column(records, "SF_OTHER_PREVENTION")] = "Work";
        records[3][Column(records, "SF_CHALLENGES")] = "Challenges";
        records[3][Column(records, "SF_DHSC_SUPPORT")] = string.Empty;
        records[3][Column(records, "SF_LEARNINGS")] = "Perhaps";

        var response = await UploadAsync(collectionId, Save(records), "my report.csv");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await response.ToResponseModel<UploadInvalidEnvelope>();
        body.Error.Should().Be("OFFLINE_UPLOAD_INVALID");
        body.ModuleCode.Should().Be(FeedbackModuleCode);
        body.ErrorCount.Should().Be(2);
        body.Errors.Should().Contain(row => row.Row == 4 && row.Column == "E" && row.Question == "Q3");
        body.Errors.Should().Contain(row => row.Row == 4 && row.Column == "F" && row.Message == "Enter ‘Yes’ or ‘No’");
        body.AnnotatedFile.FileName.Should().Be("my report-with-errors.csv");

        var annotated = Records(Convert.FromBase64String(body.AnnotatedFile.ContentBase64));
        var errors = annotated[3][Column(annotated, "errors")];
        errors.Should().Contain("Q4 (column F): Enter ‘Yes’ or ‘No’");
        annotated[3][Column(annotated, "SF_LEARNINGS")].Should().Be("Perhaps", "the person's own answers are left as they typed them");

        var feedback = await GetStrategicFeedbackAsync(collectionId);
        feedback.Answers.Should().ContainSingle().Which.Value.Should().Be("Earlier answer", "nothing from an invalid file is saved");
        feedback.SectionStatus.Should().Be("in_progress");
    }

    // BLUM-8747: the upload's audit row is written inside the upload's transaction, so a refused file (which saves
    // nothing) leaves no row, and an accepted one leaves exactly one.
    [Fact]
    public async Task ShouldAuditAnAcceptedUpload_AndNotARefusedOne()
    {
        await EnableAsync();
        AuthenticateAsMainOrgLead();
        var collectionId = await StartConfirmedReportAsync();
        var records = Records(await DownloadAsync(collectionId, StrategicFeedback));
        records[3][Column(records, "SF_OTHER_PREVENTION")] = "Work";
        records[3][Column(records, "SF_CHALLENGES")] = "Challenges";
        records[3][Column(records, "SF_DHSC_SUPPORT")] = "More guidance";
        records[3][Column(records, "SF_LEARNINGS")] = "Perhaps";

        var refused = await UploadAsync(collectionId, Save(records));

        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await Context.AuditLogs.AsNoTracking().AnyAsync(a => a.Action == AuditAction.SubmissionOfflineReportUploaded))
            .Should().BeFalse("a refused file saves nothing, so nothing is audited");

        records[3][Column(records, "SF_LEARNINGS")] = "No";
        var accepted = await UploadAsync(collectionId, Save(records));

        accepted.StatusCode.Should().Be(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());
        var submissionId = await GetSubmissionIdAsync(collectionId);
        var rows = await Context.AuditLogs.AsNoTracking()
            .Where(a => a.Action == AuditAction.SubmissionOfflineReportUploaded)
            .ToListAsync();
        var row = rows.Should().ContainSingle().Subject;
        row.TargetType.Should().Be(AuditTargetType.Submission);
        row.TargetId.Should().Be(submissionId.Value);
        row.OrganisationId.Should().Be(MainOrg.Id);
        row.After.Should().Contain(FeedbackModuleCode).And.NotContain("More guidance");
    }

    [Fact]
    public async Task ShouldReportAFollowUpAnsweredWithoutItsTrigger()
    {
        await EnableAsync();
        AuthenticateAsMainOrgLead();
        var collectionId = await CreateOpenCollectionAsync();
        var activity = await CreateRnaActivityAsync();
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);
        var records = Records(await DownloadAsync(collectionId, ActivityResults));
        var row = records.Single(cells => cells[0] == activity.Id.ToString());
        row[Column(records, "RNA_CONTRIBUTORS@active")] = "Licensing";
        row[Column(records, "RNA_CONTRIBUTORS_OTHER@active")] = "The coroner";

        var response = await UploadAsync(collectionId, Save(records));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await response.ToResponseModel<UploadInvalidEnvelope>();
        body.Errors.Should().Contain(error => error.Message.StartsWith("You answered Q2 but did not choose ‘Other’ in Q1"));
        (await Context.SubmissionActivityAnswers.AsNoTracking().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task ShouldRefuseATemplateDownloadedBeforeTheActivitiesChanged()
    {
        await EnableAsync();
        AuthenticateAsMainOrgLead();
        var collectionId = await StartConfirmedReportAsync();
        var template = await DownloadAsync(collectionId, ActivityResults);
        await CreateRnaActivityAsync(title: "Added after the download");

        var response = await UploadAsync(collectionId, template);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ToResponseModel<ErrorEnvelope>()).Error.Should().Be("OFFLINE_TEMPLATE_OUT_OF_DATE");
    }

    [Fact]
    public async Task ShouldRefuseFilesThatAreNotThisReportsTemplate()
    {
        await EnableAsync();
        AuthenticateAsMainOrgLead();
        var otherCollectionId = await CreateOpenCollectionAsync("Another Q1 report");
        var collectionId = await StartConfirmedReportAsync();
        await CreateSubmissionAsync(otherCollectionId);
        await ConfirmActivitiesAsync(otherCollectionId);
        var otherTemplate = await DownloadAsync(otherCollectionId, StrategicFeedback);

        var cases = new (byte[] Content, string FileName, string Code)[]
        {
            (otherTemplate, "report.csv", "OFFLINE_WRONG_REPORT"),
            (Encoding.UTF8.GetBytes("name,answer\r\nx,y"), "report.csv", "OFFLINE_NOT_A_TEMPLATE"),
            ([], "report.csv", "OFFLINE_FILE_EMPTY"),
            ([0x50, 0x4B, 0x03, 0x04, 0x14, 0x00], "report.xlsx", "OFFLINE_FILE_NOT_CSV"),
            (Encoding.UTF8.GetBytes("\"unterminated"), "report.csv", "OFFLINE_FILE_UNREADABLE"),
        };

        foreach (var (content, fileName, code) in cases)
        {
            var response = await UploadAsync(collectionId, content, fileName);
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest, code);
            var body = await response.ToResponseModel<ErrorEnvelope>();
            body.Error.Should().Be(code);
            body.Message.Should().NotBeNullOrWhiteSpace();
        }

        // A form with no file part, as a browser sends when nothing was chosen.
        var noFile = await Client.PostAsync(
            $"quarterly-report/{collectionId}/offline/upload",
            new MultipartFormDataContent { { new StringContent("nothing chosen"), "note" } });
        noFile.StatusCode.Should().Be(HttpStatusCode.BadRequest, await noFile.Content.ReadAsStringAsync());
        (await noFile.ToResponseModel<ErrorEnvelope>()).Error.Should().Be("OFFLINE_FILE_MISSING");
    }

    [Fact]
    public async Task ShouldRefuseBothDirections_UntilTheActivitiesAreConfirmed()
    {
        await EnableAsync();
        AuthenticateAsMainOrgLead();
        var collectionId = await CreateOpenCollectionAsync();
        await CreateRnaActivityAsync();
        await CreateSubmissionAsync(collectionId);

        var template = await Client.GetAsync($"quarterly-report/{collectionId}/offline/{StrategicFeedback}/template");
        var upload = await UploadAsync(collectionId, Encoding.UTF8.GetBytes("a,b"));

        template.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await template.ToResponseModel<ErrorEnvelope>()).Error.Should().Be("ACTIVITIES_NOT_CONFIRMED");
        upload.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await upload.ToResponseModel<ErrorEnvelope>()).Error.Should().Be("ACTIVITIES_NOT_CONFIRMED");
    }

    [Fact]
    public async Task ShouldRefuseBothDirections_OnceTheWindowHasClosed()
    {
        await EnableAsync();
        AuthenticateAsMainOrgLead();
        var collectionId = await StartConfirmedReportAsync();
        var template = await DownloadAsync(collectionId, StrategicFeedback);
        await CloseCollectionAsync(collectionId);
        AuthenticateAsMainOrgLead();

        var download = await Client.GetAsync($"quarterly-report/{collectionId}/offline/{StrategicFeedback}/template");
        var upload = await UploadAsync(collectionId, template);

        (await download.ToResponseModel<ErrorEnvelope>()).Error.Should().Be("REPORT_NOT_OPEN");
        (await upload.ToResponseModel<ErrorEnvelope>()).Error.Should().Be("REPORT_NOT_OPEN");
    }

    [Fact]
    public async Task ShouldRefuseBothDirections_OnceTheReportIsSubmitted()
    {
        await EnableAsync();
        AuthenticateAsMainOrgLead();
        var collectionId = await CreateOpenCollectionAsync();
        var activity = await CreateRnaActivityAsync();
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);
        var template = await DownloadAsync(collectionId, StrategicFeedback);
        await CompleteActivityReportAsync(collectionId, activity.Id);
        await SubmitStartedReportAsync(collectionId);

        var download = await Client.GetAsync($"quarterly-report/{collectionId}/offline/{StrategicFeedback}/template");
        var upload = await UploadAsync(collectionId, template);

        download.StatusCode.Should().Be(HttpStatusCode.Conflict);
        upload.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ShouldNotOfferATemplateForASectionTheReportDoesNotHave()
    {
        await EnableAsync();
        AuthenticateAsMainOrgLead();
        var collectionId = await CreateQuarterlyCollectionAsync(
            "No funding section", Today.AddDays(-1), Today.AddDays(60), formModuleIds: await FormModuleIdsExceptAsync(FundingModuleCode));
        await CreateRnaActivityAsync();
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);

        var response = await Client.GetAsync($"quarterly-report/{collectionId}/offline/{FundingSpent}/template");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.ToResponseModel<ErrorEnvelope>()).Error.Should().Be("OFFLINE_SECTION_NOT_IN_REPORT");
    }

    [Fact]
    public async Task TransactionRunner_ShouldKeepNothing_WhenTheWorkFailsOrThrows()
    {
        await EnableAsync();
        AuthenticateAsMainOrgLead();
        await StartConfirmedReportAsync();
        var submissionId = (await Context.Submissions.AsNoTracking().SingleAsync()).Id;

        async Task WriteAnswerAsync(IServiceProvider services, CancellationToken token)
        {
            var answer = SubmissionFeedbackAnswer.Record(SubmissionFeedbackAnswerId.New(), submissionId, "SF_DHSC_SUPPORT", "Rolled back", global::Tests.Data.AdminsData.MainAdmin.Id, DateTime.UtcNow);
            await services.GetRequiredService<ISubmissionFeedbackAnswerRepository>().ReplaceForQuestionsAsync(submissionId, ["SF_DHSC_SUPPORT"], [answer], token);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var runner = scope.ServiceProvider.GetRequiredService<ITransactionRunner>();
            var result = await runner.RunAsync<SubmissionException, Unit>(
                async token =>
                {
                    await WriteAnswerAsync(scope.ServiceProvider, token);
                    return new SubmissionAlreadySubmittedException(submissionId);
                },
                CancellationToken.None);
            result.IsLeft.Should().BeTrue();
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var runner = scope.ServiceProvider.GetRequiredService<ITransactionRunner>();
            var run = () => runner.RunAsync<SubmissionException, Unit>(
                async token =>
                {
                    await WriteAnswerAsync(scope.ServiceProvider, token);
                    throw new InvalidOperationException("Fails after writing");
                },
                CancellationToken.None);
            await run.Should().ThrowAsync<InvalidOperationException>();
        }

        (await Context.SubmissionFeedbackAnswers.AsNoTracking().AnyAsync()).Should().BeFalse();
    }
}