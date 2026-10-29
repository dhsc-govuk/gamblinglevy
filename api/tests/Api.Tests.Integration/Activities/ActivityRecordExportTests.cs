using System.Net;
using System.Net.Http.Json;
using Api.Dtos;
using Api.Services.ActivityRecord;
using Api.Services.ReportSnapshot;
using Api.Tests.Integration.QuarterlyReports;
using Application.Common.Csv;
using Domain.Activities;
using Domain.Submissions;
using FluentAssertions;
using Tests.Common;
using Tests.Data;
using Xunit;

namespace Api.Tests.Integration.Activities;

// "Download activity record" (BLUM-7648): GET activities/{id}/export restates every field of the activity record and
// then what the organisation's submitted quarterly reports recorded for it. Built on QuarterlyReportTestBase rather
// than ActivityTestBase because it needs the seeded activity form schema (ActivityTestBase clears it) and the report
// flow helpers.
public class ActivityRecordExportTests(IntegrationTestWebFactory factory) : QuarterlyReportTestBase(factory)
{
    private const string JointPartnershipTaskName = "Joint partnership";

    private static readonly string[] ExpectedHeader = ["Local authority", "Activity", "Report", "Section", "Question", "Answer"];

    [Fact]
    public async Task ShouldDownloadACsvNamedForTheOrganisationAndActivity()
    {
        AuthenticateAsMainOrgLead();
        var activity = await CreateRnaActivityAsync(title: "Community outreach");

        var response = await Client.GetAsync($"activities/{activity.Id}/export");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        var disposition = response.Content.Headers.ContentDisposition!;
        (disposition.FileNameStar ?? disposition.FileName).Should().Be($"{ExportFileName.NameSegment(MainOrg.Name)}-Community-outreach-activity-record.csv");

        var lines = ParseLines(await response.Content.ReadAsStringAsync());
        lines[0].Should().Equal(ExpectedHeader);
        lines.Skip(1).Should().OnlyContain(cells => cells.Length == ExpectedHeader.Length && cells[0] == MainOrg.Name && cells[1] == "Community outreach");
    }

    [Fact]
    public async Task ShouldListEveryCreationFieldInPageOrder_InTheWordsTheRecordWasAnsweredIn()
    {
        AuthenticateAsMainOrgLead();
        var (categoryId, taskId) = await GetTypeAsync(RnaTaskName);
        var results = await StandardResultsAsync(taskId, "rna_contributors", "rna_published");
        var activity = await CreateCompletedActivityAsync(
            RnaTaskName,
            "active",
            results,
            [new("NA_WHO_WITH", "licensing,other"), new("NA_WHO_WITH_OTHER", "Local charity"), new("NA_PLAN", "Publish in the autumn")],
            title: "Regional assessment");
        var schema = await GetSchemaAsync(categoryId);
        var taxonomy = await GetTaxonomyAsync();

        var rows = await ExportRowsAsync(activity.Id);

        rows.Should().OnlyContain(row => row.Report == string.Empty, "no report has been submitted");
        rows.Select(row => row.Section).Distinct().Should().Equal(
            "Activity record",
            "Activity category",
            "Activity type",
            "Activity details",
            "Expected results",
            "Additional information",
            "Activity status");

        var whoWith = schema.Questions.Single(q => q.Code == "NA_WHO_WITH");
        var licensing = whoWith.Options.Single(o => o.Value == "licensing").Label;
        var expectedLabels = results.Select(r => taxonomy.KeyResults.First(k => k.Code == r.KeyResultCode).Label);

        rows.Should().ContainInOrder(
            new ExportRow(string.Empty, "Activity record", "Activity ID", activity.Id.ToString()),
            new ExportRow(string.Empty, "Activity record", "Record status", "Complete"),
            new ExportRow(string.Empty, "Activity category", ActivityRecordPresenter.CategoryQuestion, "Needs assessment"),
            new ExportRow(string.Empty, "Activity type", "What type of work does it involve?", RnaTaskName),
            new ExportRow(string.Empty, "Activity details", ActivityRecordPresenter.ActivityNameQuestion, "Regional assessment"),
            new ExportRow(string.Empty, "Activity details", whoWith.Label, $"{licensing}, Other"),
            new ExportRow(string.Empty, "Activity details", Label(schema, "NA_WHO_WITH_OTHER"), "Local charity"),
            new ExportRow(string.Empty, "Expected results", ActivityRecordPresenter.ExpectedResultsQuestion, string.Join(", ", expectedLabels)),
            new ExportRow(string.Empty, "Additional information", Label(schema, "NA_PLAN"), "Publish in the autumn"),
            new ExportRow(string.Empty, "Activity status", "What’s the status of this activity?", "Active"),
            new ExportRow(string.Empty, "Activity status", "When did the work start?", "April 2026"));
    }

    // BLUM-7673, end to end: the stored reason reaches the file through the activity DTO.
    [Fact]
    public async Task ShouldListTheClosureReasonAfterTheStatus_ForAClosedRecord()
    {
        AuthenticateAsMainOrgLead();
        var activity = await CreateCompletedActivityAsync(RnaTaskName, "closed");

        var rows = await ExportRowsAsync(activity.Id);

        rows.Where(row => row.Section == ActivityRecordPresenter.ActivityStatusSection).Should().Equal(
            new ExportRow(string.Empty, "Activity status", "What’s the status of this activity?", "Closed"),
            new ExportRow(string.Empty, "Activity status", "Why is this activity being closed?", "No longer needed"));
    }

    [Fact]
    public async Task ShouldLeaveOutAConditionalQuestionItsParentAnswerDidNotReveal()
    {
        AuthenticateAsMainOrgLead();
        var (categoryId, _) = await GetTypeAsync(RnaTaskName);
        var activity = await CreateCompletedActivityAsync(answers: [new("NA_WHO_WITH", "licensing")]);
        var schema = await GetSchemaAsync(categoryId);

        var rows = await ExportRowsAsync(activity.Id);

        rows.Should().Contain(row => row.Question == Label(schema, "NA_WHO_WITH"));
        rows.Should().NotContain(row => row.Question == Label(schema, "NA_WHO_WITH_OTHER"));
    }

    // BLUM-8090: the page shows a partnership's fixed status read-only and asks for no name and no start date, so the
    // file does the same. The record here still carries a name and a start date, as one created before that change
    // does (CreateCompletedActivityAsync sends both) — neither may resurface as a row nobody can change.
    [Fact]
    public async Task ShouldDescribeAJointPartnershipByItsFixedStatus_AndShowTheCommitmentInPounds()
    {
        AuthenticateAsMainOrgLead();
        var (categoryId, _) = await GetTypeAsync(JointPartnershipTaskName);
        var activity = await CreateCompletedActivityAsync(JointPartnershipTaskName, "active", [], [new("JP_COMMITTED", "25000")], title: "Pooled prevention fund");
        var schema = await GetSchemaAsync(categoryId);

        var rows = await ExportRowsAsync(activity.Id);

        rows.Should().Contain(new ExportRow(string.Empty, "Activity details", Label(schema, "JP_COMMITTED"), "£25,000"));
        rows.Should().Contain(new ExportRow(string.Empty, "Activity status", "What’s the status of this activity?", "Active"));
        rows.Should().NotContain(row => row.Question == ActivityRecordPresenter.ActivityNameQuestion);
        rows.Should().NotContain(row => row.Question.StartsWith("When did"));
        rows.Should().NotContain(row => row.Section == "Activity type" || row.Section == "Start date" || row.Section == "Expected results");
    }

    // A partnership's delivery answers are labelled in the partnership wording the report asked them in (BLUM-8090),
    // not the module's generic copy.
    [Fact]
    public async Task ShouldLabelAJointPartnershipsReportedDelivery_InThePartnershipWording()
    {
        AuthenticateAsMainOrgLead();
        var activity = await CreateCompletedActivityAsync(JointPartnershipTaskName, "active", []);
        var collectionId = await CreateOpenQuarterCollectionAsync(1, "Q1 report");
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);
        (await SaveAnswersAsync(collectionId, activity.Id, saveAsDraft: true, ("AKR_DELIVERY_PROGRESS", "Agreed the pooled budget"))).EnsureSuccessStatusCode();
        (await CompleteActivityAsync(collectionId, activity.Id)).EnsureSuccessStatusCode();
        await SubmitStartedReportAsync(collectionId);

        var rows = await ExportRowsAsync(activity.Id);

        var report = $"Q1 {TestFinancialYears.Current}";
        rows.Should().Contain(new ExportRow(report, "Activity delivery", "What progress have you made this quarter?", "Agreed the pooled budget"));
    }

    [Fact]
    public async Task ShouldDescribeARoleInRoleWording_WithoutAnActivityName()
    {
        AuthenticateAsMainOrgLead();
        var (categoryId, taskId) = await GetTypeAsync(PeopleTaskName);
        var created = await Client.PostAsJsonAsync(
            "activities",
            new CreateActivityRequestDto(
                "Public health officer role",
                categoryId,
                taskId,
                null,
                "active",
                4,
                2026,
                false,
                [],
                [new("PR_FUNDING_TIME", "up_to_20"), new("PR_ROLE_DURATION", "6_12")]));
        created.IsSuccessStatusCode.Should().BeTrue("creating the role failed: {0}", await created.Content.ReadAsStringAsync());
        var activity = await created.ToResponseModel<ActivityDto>();
        var schema = await GetSchemaAsync(categoryId);

        var rows = await ExportRowsAsync(activity.Id);

        rows.Should().Contain(new ExportRow(string.Empty, "Role type", "What's the role?", PeopleTaskName));
        rows.Should().Contain(row => row.Section == "Role details" && row.Question == Label(schema, "PR_FUNDING_TIME"));
        rows.Should().Contain(new ExportRow(string.Empty, "Activity status", "What's the status of this role?", "Active"));
        rows.Should().Contain(new ExportRow(string.Empty, "Activity status", "When did the staff member start work?", "April 2026"));
        rows.Should().NotContain(row => row.Question == ActivityRecordPresenter.ActivityNameQuestion);
        rows.Should().NotContain(row => row.Section == "Additional information" || row.Section == "Activity details");
    }

    [Fact]
    public async Task ShouldDownloadADraft_AsADraftWithNothingReported()
    {
        AuthenticateAsMainOrgLead();
        var (categoryId, taskId) = await GetTypeAsync(RnaTaskName);
        var created = await Client.PostAsJsonAsync(
            "activities",
            new CreateActivityRequestDto("Half-planned idea", categoryId, taskId, null, null, null, null, true, [], []));
        created.IsSuccessStatusCode.Should().BeTrue("saving the draft failed: {0}", await created.Content.ReadAsStringAsync());
        var draft = await created.ToResponseModel<ActivityDto>();

        var rows = await ExportRowsAsync(draft.Id);

        rows.Should().Contain(new ExportRow(string.Empty, "Activity record", "Record status", "Draft"));
        rows.Should().Contain(new ExportRow(string.Empty, "Activity status", "What’s the status of this activity?", "Draft"));
        rows.Should().OnlyContain(row => row.Report == string.Empty);
    }

    [Fact]
    public async Task ShouldIncludeWhatASubmittedReportRecorded_AndNothingFromAReportStillInProgress()
    {
        AuthenticateAsMainOrgLead();
        var activity = await CreateRnaActivityAsync(status: "finished", title: "Regional assessment");
        var firstQuarter = await CreateOpenQuarterCollectionAsync(1, "Q1 report");
        await SubmitFirstQuarterReportAsync(firstQuarter, activity.Id);

        // A second report, started and answered but never submitted. Confirming it also carries the finished
        // activity's Q1 answers forward into draft rows (BLUM-7671) — which must not surface as a second report.
        var secondQuarter = await CreateOpenQuarterCollectionAsync(2, "Q2 report");
        await CreateSubmissionAsync(secondQuarter);
        await ConfirmActivitiesAsync(secondQuarter);
        (await SaveAnswersAsync(secondQuarter, activity.Id, saveAsDraft: true, ("AKR_DELIVERY_PROGRESS", "Draft progress for Q2"))).EnsureSuccessStatusCode();
        var questions = (await GetActivityQuestionsAsync(secondQuarter, activity.Id)).Questions;

        var rows = await ExportRowsAsync(activity.Id);

        var report = $"Q1 {TestFinancialYears.Current}";
        rows.Where(row => row.Report != string.Empty).Should().OnlyContain(row => row.Report == report);
        rows.Should().NotContain(row => row.Answer == "Draft progress for Q2");
        rows.Where(row => row.Report == report).Should().ContainInOrder(
            new ExportRow(report, "Quarterly report", "Reference number", TestFinancialYears.Reference(TestFinancialYears.Current, 1, 1)),
            new ExportRow(report, "Activity results", LabelOf(questions, "RNA_PUBLISHED"), "Yes"),
            new ExportRow(report, "Activity results", LabelOf(questions, "RNA_PUBLISHED_URL"), "https://example.org/assessment"),
            new ExportRow(report, "Activity delivery", LabelOf(questions, "AKR_DELIVERY_PROGRESS"), "Published the assessment"),
            new ExportRow(report, "Funding spend", ActivityRecordPresenter.ActualSpendQuestion, "100.00"),
            new ExportRow(report, "Funding spend", "Q2 forecast (£)", "50.00"),
            new ExportRow(report, "Funding spend", "Q3 forecast (£)", "50.00"),
            new ExportRow(report, "Funding spend", "Q4 forecast (£)", "50.00"));
    }

    [Fact]
    public async Task ShouldLeaveOutAnswersFromAReportTheActivityWasNotCompletedIn()
    {
        AuthenticateAsMainOrgLead();
        var reported = await CreateRnaActivityAsync(title: "Reported activity");
        var collectionId = await CreateOpenQuarterCollectionAsync(1, "Q1 report");
        await SubmitFirstQuarterReportAsync(collectionId, reported.Id);

        // An answer row with no Completed report behind it — the shape a carried-forward or since-unreportable
        // activity leaves in a submitted report. It was never part of what was submitted.
        var unreported = await CreateRnaActivityAsync(title: "Unreported activity");
        await Context.SubmissionActivityAnswers.AddAsync(SubmissionActivityAnswer.Record(
            SubmissionActivityAnswerId.New(),
            await GetSubmissionIdAsync(collectionId),
            new ActivityId(unreported.Id),
            "AKR_DELIVERY_PROGRESS",
            "Never submitted",
            AdminsData.MainAdmin.Id,
            DateTime.UtcNow));
        await SaveChangesAsync();

        var rows = await ExportRowsAsync(unreported.Id);

        rows.Should().OnlyContain(row => row.Report == string.Empty);
        rows.Should().NotContain(row => row.Answer == "Never submitted");
    }

    [Fact]
    public async Task ShouldNeutraliseASpreadsheetFormulaTypedIntoAnAnswer()
    {
        AuthenticateAsMainOrgLead();
        var activity = await CreateCompletedActivityAsync(answers: [new("NA_PLAN", "=SUM(A1:A9)")]);

        var response = await Client.GetAsync($"activities/{activity.Id}/export");
        response.EnsureSuccessStatusCode();
        var csv = await response.Content.ReadAsStringAsync();

        csv.Should().Contain("'=SUM(A1:A9)");
        ParseLines(csv).Should().NotContain(cells => cells.Any(cell => cell.StartsWith('=')));
    }

    [Fact]
    public async Task ShouldNotExposeAnotherOrganisationsActivity()
    {
        AuthenticateAsMainOrgLead();
        var activity = await CreateRnaActivityAsync();

        AuthenticateAsSecondOrgLead();
        var response = await Client.GetAsync($"activities/{activity.Id}/export");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ShouldNotExportADeletedActivity()
    {
        AuthenticateAsMainOrgLead();
        var activity = await CreateRnaActivityAsync();
        (await Client.DeleteAsync($"activities/{activity.Id}")).EnsureSuccessStatusCode();

        var response = await Client.GetAsync($"activities/{activity.Id}/export");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static string Label(FormSchemaDto schema, string code) => schema.Questions.Single(q => q.Code == code).Label;

    private static string LabelOf(IEnumerable<ReportQuestionDto> questions, string code) => questions.Single(q => q.Code == code).Label;

    // The test data carries no line breaks, so a CSV line is a record.
    private static List<string[]> ParseLines(string csv) =>
        csv.Split("\r\n").Select(CsvParser.ParseCsvLine).ToList();

    private async Task SubmitFirstQuarterReportAsync(Guid collectionId, Guid activityId)
    {
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);
        await CompleteFinishedRnaReportAsync(collectionId, activityId, "https://example.org/assessment", "Published the assessment");
        await SubmitStartedReportAsync(collectionId);
    }

    private async Task<FormSchemaDto> GetSchemaAsync(Guid categoryId)
    {
        var response = await Client.GetAsync($"activities/form-schema?categoryId={categoryId}");
        response.EnsureSuccessStatusCode();
        return await response.ToResponseModel<FormSchemaDto>();
    }

    private async Task<List<ExportRow>> ExportRowsAsync(Guid activityId)
    {
        var response = await Client.GetAsync($"activities/{activityId}/export");
        response.IsSuccessStatusCode.Should().BeTrue("exporting the activity record failed: {0}", await response.Content.ReadAsStringAsync());

        return ParseLines(await response.Content.ReadAsStringAsync())
            .Skip(1)
            .Select(cells => new ExportRow(cells[2], cells[3], cells[4], cells[5]))
            .ToList();
    }

    private sealed record ExportRow(string Report, string Section, string Question, string Answer);
}