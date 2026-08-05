using System.Net;
using Api.Dtos;
using Domain.Activities;
using Domain.Submissions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Tests.Common;
using Xunit;

namespace Api.Tests.Integration.QuarterlyReports;

// PUT quarterly-report/{collectionId}/activities/{activityId}/answers — saving one activity's key-result answers,
// the per-activity report status and the "Enter activity key results" section roll-up.
public class SaveActivityReportTests(IntegrationTestWebFactory factory) : QuarterlyReportTestBase(factory)
{
    private sealed record AnswerErrorRow(string QuestionCode, string Message);

    private sealed record AnswersInvalidEnvelope(string Error, string Message, List<AnswerErrorRow> AnswerErrors);

    private sealed record ErrorEnvelope(string Error, string Message);

    // Open collection + RNA activity (rna_contributors / rna_published) + confirmed submission. The caller must
    // already be authenticated as a Main-org activity manager: identity is AsyncLocal-scoped, so a helper cannot
    // switch it for its caller — every test sets it in its own body first (the repo-wide convention).
    private async Task<(Guid CollectionId, ActivityDto Activity)> StartConfirmedReportAsync()
    {
        var collectionId = await CreateOpenCollectionAsync();
        var activity = await CreateRnaActivityAsync();
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);
        return (collectionId, activity);
    }

    [Fact]
    public async Task DraftSave_WithMissingRequiredAnswers_SavesInProgress()
    {
        AuthenticateAsMainOrgLead();
        var (collectionId, activity) = await StartConfirmedReportAsync();

        var response = await SaveAnswersAsync(
            collectionId, activity.Id, saveAsDraft: true, ("AKR_DELIVERY_PROGRESS", "Half-way there"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var state = await response.ToResponseModel<ActivityReportStateDto>();
        state.ActivityId.Should().Be(activity.Id);
        state.ReportStatus.Should().Be("in_progress");

        var report = await Context.SubmissionActivityReports.AsNoTracking().SingleAsync();
        report.ActivityId.Should().Be(new ActivityId(activity.Id));
        report.Status.Should().Be(SubmissionSectionStatus.InProgress);

        var taskList = await GetTaskListAsync(collectionId);
        taskList.Sections.Single(s => s.ModuleCode == KeyResultsModuleCode).Status.Should().Be("in_progress");
    }

    // A non-draft PUT still validates required questions against the submitted map (vestigial — the frontend
    // always saves drafts and completes via the complete endpoint), and persists nothing on failure.
    [Fact]
    public async Task NonDraftSave_MissingARequiredAnswer_Returns422WithTheProtocolMessage()
    {
        AuthenticateAsMainOrgLead();
        var (collectionId, activity) = await StartConfirmedReportAsync();

        // RNA_CONTRIBUTORS (required, active phrasing) is omitted.
        var response = await SaveAnswersAsync(
            collectionId, activity.Id, saveAsDraft: false, ("AKR_DELIVERY_PROGRESS", "Done"));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var envelope = await response.ToResponseModel<AnswersInvalidEnvelope>();
        envelope.Error.Should().Be("SUBMISSION_ANSWERS_INVALID");
        envelope.AnswerErrors.Should().Contain(e =>
            e.QuestionCode == "RNA_CONTRIBUTORS" && e.Message == "Select who is contributing evidence");

        (await Context.SubmissionActivityAnswers.AsNoTracking().AnyAsync()).Should().BeFalse();
    }

    // Type errors block even draft saves so garbage never persists. The 3 July protocol (BLUM-6933) removed
    // every numeric question, so the surviving type rule to exercise is list-option validity — LSG_MEMBERS is
    // a prefilled list question.
    [Fact]
    public async Task InvalidListOption_Returns422EvenOnADraftSave()
    {
        var collectionId = await CreateOpenCollectionAsync();
        AuthenticateAsMainOrgLead();
        var (_, taskId) = await GetTypeAsync(SteeringGroupTaskName);
        var results = await StandardResultsAsync(taskId, "lsg_members");
        var activity = await CreateCompletedActivityAsync(SteeringGroupTaskName, "active", results);
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);

        var response = await SaveAnswersAsync(collectionId, activity.Id, saveAsDraft: true, ("LSG_MEMBERS", "not_a_seeded_option"));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var envelope = await response.ToResponseModel<AnswersInvalidEnvelope>();
        envelope.Error.Should().Be("SUBMISSION_ANSWERS_INVALID");
        envelope.AnswerErrors.Should().Contain(e => e.QuestionCode == "LSG_MEMBERS");
    }

    [Fact]
    public async Task Complete_WithAllRequiredAnswersSaved_MarksTheActivityCompleted()
    {
        AuthenticateAsMainOrgLead();
        var (collectionId, activity) = await StartConfirmedReportAsync();
        (await SaveAnswersAsync(
            collectionId,
            activity.Id,
            saveAsDraft: true,
            ("RNA_CONTRIBUTORS", "licensing"),
            ("AKR_DELIVERY_PROGRESS", "All milestones reached"))).EnsureSuccessStatusCode();

        var response = await CompleteActivityAsync(collectionId, activity.Id);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var state = await response.ToResponseModel<ActivityReportStateDto>();
        state.ReportStatus.Should().Be("completed");

        var report = await Context.SubmissionActivityReports.AsNoTracking().SingleAsync();
        report.Status.Should().Be(SubmissionSectionStatus.Completed);

        var answers = await Context.SubmissionActivityAnswers.AsNoTracking().ToListAsync();
        answers.Should().HaveCount(2);
        answers.Should().Contain(a => a.QuestionCode == "RNA_CONTRIBUTORS" && a.Value == "licensing");
    }

    [Fact]
    public async Task Complete_WithMissingSavedAnswers_Returns422AndDoesNotComplete()
    {
        AuthenticateAsMainOrgLead();
        var (collectionId, activity) = await StartConfirmedReportAsync();
        (await SaveAnswersAsync(
            collectionId, activity.Id, saveAsDraft: true, ("AKR_DELIVERY_PROGRESS", "Done"))).EnsureSuccessStatusCode();

        var response = await CompleteActivityAsync(collectionId, activity.Id);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var envelope = await response.ToResponseModel<AnswersInvalidEnvelope>();
        envelope.Error.Should().Be("SUBMISSION_ANSWERS_INVALID");
        envelope.AnswerErrors.Should().Contain(e =>
            e.QuestionCode == "RNA_CONTRIBUTORS" && e.Message == "Select who is contributing evidence");

        // The saved draft answer is untouched and the report stays incomplete.
        var answers = await Context.SubmissionActivityAnswers.AsNoTracking().ToListAsync();
        answers.Should().ContainSingle(a => a.QuestionCode == "AKR_DELIVERY_PROGRESS");
        var report = await Context.SubmissionActivityReports.AsNoTracking().SingleAsync();
        report.Status.Should().Be(SubmissionSectionStatus.InProgress);
    }

    // The key pin of the per-page contract: a non-draft PUT validates but never completes the report.
    [Fact]
    public async Task NonDraftSave_WithAllRequiredAnswers_DoesNotCompleteTheReport()
    {
        AuthenticateAsMainOrgLead();
        var (collectionId, activity) = await StartConfirmedReportAsync();

        var response = await SaveAnswersAsync(
            collectionId,
            activity.Id,
            saveAsDraft: false,
            ("RNA_CONTRIBUTORS", "licensing"),
            ("AKR_DELIVERY_PROGRESS", "All milestones reached"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var state = await response.ToResponseModel<ActivityReportStateDto>();
        state.ReportStatus.Should().Be("in_progress");
    }

    [Fact]
    public async Task SaveDraft_AfterCompleting_DropsTheReportBackToInProgress()
    {
        AuthenticateAsMainOrgLead();
        var (collectionId, activity) = await StartConfirmedReportAsync();
        await CompleteActivityReportAsync(collectionId, activity.Id);

        var response = await SaveAnswersAsync(
            collectionId, activity.Id, saveAsDraft: true, ("AKR_DELIVERY_PROGRESS", "Revised progress"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var report = await Context.SubmissionActivityReports.AsNoTracking().SingleAsync();
        report.Status.Should().Be(SubmissionSectionStatus.InProgress);

        var taskList = await GetTaskListAsync(collectionId);
        taskList.Sections.Single(s => s.ModuleCode == KeyResultsModuleCode).Status.Should().Be("in_progress");
    }

    [Fact]
    public async Task CompletingEveryReportableActivity_FlipsTheSectionToCompleted()
    {
        var collectionId = await CreateOpenCollectionAsync();
        AuthenticateAsMainOrgLead();
        var first = await CreateRnaActivityAsync(title: "First activity");
        var second = await CreateRnaActivityAsync(title: "Second activity");
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);

        await CompleteActivityReportAsync(collectionId, first.Id);

        var partial = await GetTaskListAsync(collectionId);
        partial.Sections.Single(s => s.ModuleCode == KeyResultsModuleCode).Status.Should().Be("in_progress");

        await CompleteActivityReportAsync(collectionId, second.Id);

        var complete = await GetTaskListAsync(collectionId);
        complete.Sections.Single(s => s.ModuleCode == KeyResultsModuleCode).Status.Should().Be("completed");
    }

    // The per-page merge contract (mirrors the strategic-feedback tests): saving one page's codes never
    // touches the other page's saved answers.
    [Fact]
    public async Task SaveDraft_SavingOneSetOfCodes_DoesNotWipeTheOtherSavedAnswers()
    {
        AuthenticateAsMainOrgLead();
        var (collectionId, activity) = await StartConfirmedReportAsync();
        (await SaveAnswersAsync(
            collectionId, activity.Id, saveAsDraft: true, ("RNA_CONTRIBUTORS", "licensing"))).EnsureSuccessStatusCode();

        var response = await SaveAnswersAsync(
            collectionId, activity.Id, saveAsDraft: true, ("AKR_DELIVERY_PROGRESS", "Delivery going well"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var answers = await Context.SubmissionActivityAnswers.AsNoTracking().ToListAsync();
        answers.Should().HaveCount(2);
        answers.Should().Contain(a => a.QuestionCode == "RNA_CONTRIBUTORS" && a.Value == "licensing");
        answers.Should().Contain(a => a.QuestionCode == "AKR_DELIVERY_PROGRESS" && a.Value == "Delivery going well");
    }

    [Fact]
    public async Task SaveDraft_ReSavingACode_ReplacesOnlyThatCodeAndClearsBlanks()
    {
        AuthenticateAsMainOrgLead();
        var (collectionId, activity) = await StartConfirmedReportAsync();
        (await SaveAnswersAsync(
            collectionId,
            activity.Id,
            saveAsDraft: true,
            ("RNA_CONTRIBUTORS", "licensing"),
            ("AKR_DELIVERY_PROGRESS", "First pass"))).EnsureSuccessStatusCode();

        // Re-save both codes: one updated, one cleared to blank (how the page submits a field the user emptied).
        var response = await SaveAnswersAsync(
            collectionId,
            activity.Id,
            saveAsDraft: true,
            ("RNA_CONTRIBUTORS", string.Empty),
            ("AKR_DELIVERY_PROGRESS", "Second pass"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var answers = await Context.SubmissionActivityAnswers.AsNoTracking().ToListAsync();
        answers.Should().ContainSingle();
        answers[0].QuestionCode.Should().Be("AKR_DELIVERY_PROGRESS");
        answers[0].Value.Should().Be("Second pass");
    }

    [Fact]
    public async Task Save_BeforeConfirmingActivities_Returns422ActivitiesNotConfirmed()
    {
        var collectionId = await CreateOpenCollectionAsync();
        AuthenticateAsMainOrgLead();
        var activity = await CreateRnaActivityAsync();
        await CreateSubmissionAsync(collectionId);

        var response = await SaveAnswersAsync(
            collectionId, activity.Id, saveAsDraft: true, ("AKR_DELIVERY_PROGRESS", "Too early"));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var envelope = await response.ToResponseModel<ErrorEnvelope>();
        envelope.Error.Should().Be("ACTIVITIES_NOT_CONFIRMED");
    }

    [Fact]
    public async Task Save_ForAPlannedActivity_Returns422()
    {
        AuthenticateAsMainOrgLead();
        var (collectionId, _) = await StartConfirmedReportAsync();
        var planned = await CreateCompletedActivityAsync(status: "planned", title: "Planned activity");

        var response = await SaveAnswersAsync(
            collectionId, planned.Id, saveAsDraft: true, ("AKR_DELIVERY_PROGRESS", "Not reportable"));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}