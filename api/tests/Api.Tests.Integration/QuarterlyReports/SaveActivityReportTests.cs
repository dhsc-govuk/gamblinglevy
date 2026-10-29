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
// the per-activity report status and the "Enter activity results" section roll-up.
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

    // Open collection + an RNA activity whose selected key result is the information list — the one offering
    // "Other" — plus a confirmed submission. Needs assessment keeps the protocol's "select at least 2" rule, so
    // rna_published rides along; Closed status keeps the finished-only RNA_PUBLISHED_URL out of the way. Like
    // StartConfirmedReportAsync, the caller must already be authenticated as a Main-org activity manager.
    private async Task<(Guid CollectionId, ActivityDto Activity)> StartConfirmedInformationReportAsync()
    {
        var collectionId = await CreateOpenCollectionAsync();
        var (_, taskId) = await GetTypeAsync(RnaTaskName);
        var results = await StandardResultsAsync(taskId, "rna_information", "rna_published");
        var activity = await CreateCompletedActivityAsync(RnaTaskName, "closed", results);
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);
        return (collectionId, activity);
    }

    // Open collection + a Closed screening activity whose one selected key result unlocks the services list, plus a
    // confirmed submission. Help types carry standard definitions, so a single selection satisfies the completion
    // minimum. Like StartConfirmedReportAsync, the caller must already be authenticated as a Main-org activity
    // manager.
    private async Task<(Guid CollectionId, ActivityDto Activity)> StartConfirmedScreeningReportAsync()
    {
        var collectionId = await CreateOpenCollectionAsync();
        var (_, taskId) = await GetTypeAsync(ScreeningTaskName);
        var results = await StandardResultsAsync(taskId, "scr_tool");
        var activity = await CreateCompletedActivityAsync(ScreeningTaskName, "closed", results);
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

    // Open collection + a steering-group activity measuring "List of members" + a confirmed submission. Like the
    // helpers above, the caller must already be authenticated as a Main-org activity manager.
    private async Task<(Guid CollectionId, ActivityDto Activity)> StartConfirmedSteeringGroupReportAsync()
    {
        var collectionId = await CreateOpenCollectionAsync();
        var (_, taskId) = await GetTypeAsync(SteeringGroupTaskName);
        var results = await StandardResultsAsync(taskId, "lsg_members");
        var activity = await CreateCompletedActivityAsync(SteeringGroupTaskName, "active", results);
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);
        return (collectionId, activity);
    }

    // Free-text answers for the module's unconditional "Activity delivery" questions, read from the form rather
    // than hardcoded so these tests keep working when that module is edited. Without them a non-draft save is
    // refused for the delivery questions, which says nothing about the steering group.
    private static (string Code, string? Value)[] DeliveryAnswers(ActivityReportQuestionsDto form) =>
        form.Questions
            .Where(q => q.Section == "delivery" && q.IsRequired)
            .Select(q => (q.Code, (string?)"Progress made this quarter"))
            .ToArray();

    // BLUM-7788: the engagement question is asked once per member, so each ticked member owns a required question
    // that names them — both in the label and in the message, since the error summary has to say which member is
    // unanswered. A single shared "...of each steering group member" message could not.
    [Fact]
    public async Task NonDraftSave_TickingMembersWithoutTheirEngagement_Returns422PerMember()
    {
        AuthenticateAsMainOrgLead();
        var (collectionId, activity) = await StartConfirmedSteeringGroupReportAsync();
        var form = await GetActivityQuestionsAsync(collectionId, activity.Id);

        var response = await SaveAnswersAsync(
            collectionId,
            activity.Id,
            saveAsDraft: false,
            [("LSG_MEMBERS", "licensing,police"), .. DeliveryAnswers(form)]);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var envelope = await response.ToResponseModel<AnswersInvalidEnvelope>();
        envelope.AnswerErrors.Should().Contain(e =>
            e.QuestionCode == "LSG_ENGAGEMENT_LICENSING" && e.Message == "Select the level of engagement of Licensing");
        envelope.AnswerErrors.Should().Contain(e =>
            e.QuestionCode == "LSG_ENGAGEMENT_POLICE" && e.Message == "Select the level of engagement of Police");

        // The members left unticked are the whole point of the fan-out: their questions are not shown, so they are
        // not demanded either. Nothing is saved by a refused non-draft save.
        envelope.AnswerErrors.Should().NotContain(e => e.QuestionCode == "LSG_ENGAGEMENT_HOUSING");
        (await Context.SubmissionActivityAnswers.AsNoTracking().AnyAsync()).Should().BeFalse();
    }

    // The happy path: one engagement answer per ticked member, each persisted under its own question code — the
    // unique (submission, activity, question_code) index is what forces the per-member codes in the first place.
    [Fact]
    public async Task NonDraftSave_StoresOneEngagementAnswerPerTickedMember()
    {
        AuthenticateAsMainOrgLead();
        var (collectionId, activity) = await StartConfirmedSteeringGroupReportAsync();
        var form = await GetActivityQuestionsAsync(collectionId, activity.Id);

        var response = await SaveAnswersAsync(
            collectionId,
            activity.Id,
            saveAsDraft: false,
            [
                ("LSG_MEMBERS", "licensing,police"),
                ("LSG_ENGAGEMENT_LICENSING", "attends_engages"),
                ("LSG_ENGAGEMENT_POLICE", "no_engagement"),
                .. DeliveryAnswers(form),
            ]);

        response.IsSuccessStatusCode.Should().BeTrue(
            "saving the steering-group answers failed: {0}", await response.Content.ReadAsStringAsync());

        var saved = await Context.SubmissionActivityAnswers.AsNoTracking()
            .Where(a => a.ActivityId == new ActivityId(activity.Id) && a.QuestionCode.StartsWith("LSG_ENGAGEMENT_"))
            .ToListAsync();

        saved.Should().HaveCount(2);
        saved.Should().ContainSingle(a => a.QuestionCode == "LSG_ENGAGEMENT_LICENSING" && a.Value == "attends_engages");
        saved.Should().ContainSingle(a => a.QuestionCode == "LSG_ENGAGEMENT_POLICE" && a.Value == "no_engagement");
    }

    // Unticking a member hides its engagement question, and Complete replaces the activity's answers with the
    // visible set — so the answer given for a member the LA has since dropped does not survive into the submitted
    // report or the export, which renders whatever the snapshot froze.
    [Fact]
    public async Task Complete_DropsTheEngagementAnswerOfAnUntickedMember()
    {
        AuthenticateAsMainOrgLead();
        var (collectionId, activity) = await StartConfirmedSteeringGroupReportAsync();
        var form = await GetActivityQuestionsAsync(collectionId, activity.Id);

        var first = await SaveAnswersAsync(
            collectionId,
            activity.Id,
            saveAsDraft: true,
            [
                ("LSG_MEMBERS", "licensing,police"),
                ("LSG_ENGAGEMENT_LICENSING", "attends_engages"),
                ("LSG_ENGAGEMENT_POLICE", "no_engagement"),
                .. DeliveryAnswers(form),
            ]);
        first.IsSuccessStatusCode.Should().BeTrue();

        // Police is dropped from the members list; the client no longer renders its engagement question, so it
        // never posts that code again and the stale answer is left behind for Complete to prune.
        var second = await SaveAnswersAsync(collectionId, activity.Id, saveAsDraft: true, ("LSG_MEMBERS", "licensing"));
        second.IsSuccessStatusCode.Should().BeTrue();

        var complete = await CompleteActivityAsync(collectionId, activity.Id);
        complete.IsSuccessStatusCode.Should().BeTrue(
            "an unticked member's engagement question is not visible, so it is not required: {0}",
            await complete.Content.ReadAsStringAsync());

        var saved = await Context.SubmissionActivityAnswers.AsNoTracking()
            .Where(a => a.ActivityId == new ActivityId(activity.Id) && a.QuestionCode.StartsWith("LSG_ENGAGEMENT_"))
            .ToListAsync();

        saved.Should().ContainSingle()
            .Which.QuestionCode.Should().Be("LSG_ENGAGEMENT_LICENSING");
    }

    // BLUM-7472: "Other" on a protocol list reveals a free-text companion, which is required for as long as the
    // reveal is showing. Ticking "other" and leaving it blank must be refused — otherwise the answer is
    // uncapturable, since the checkbox validator also rejects free text posted into the parent code.
    [Fact]
    public async Task NonDraftSave_TickingOtherWithoutTheFreeText_Returns422()
    {
        AuthenticateAsMainOrgLead();
        var (collectionId, activity) = await StartConfirmedInformationReportAsync();

        var response = await SaveAnswersAsync(
            collectionId,
            activity.Id,
            saveAsDraft: false,
            ("RNA_INFORMATION", "prevalence,other"),
            ("RNA_PUBLISHED", "no"),
            ("AKR_DELIVERY_PROGRESS", "Drafted the assessment"));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var envelope = await response.ToResponseModel<AnswersInvalidEnvelope>();
        envelope.Error.Should().Be("SUBMISSION_ANSWERS_INVALID");
        envelope.AnswerErrors.Should().ContainSingle(e =>
            e.QuestionCode == "RNA_INFORMATION_OTHER" && e.Message == "Enter the other information covered");
    }

    [Fact]
    public async Task NonDraftSave_WithTheOtherFreeText_SucceedsAndPersistsIt()
    {
        AuthenticateAsMainOrgLead();
        var (collectionId, activity) = await StartConfirmedInformationReportAsync();

        var response = await SaveAnswersAsync(
            collectionId,
            activity.Id,
            saveAsDraft: false,
            ("RNA_INFORMATION", "prevalence,other"),
            ("RNA_INFORMATION_OTHER", "Licensing enforcement data"),
            ("RNA_PUBLISHED", "no"),
            ("AKR_DELIVERY_PROGRESS", "Drafted the assessment"));

        response.IsSuccessStatusCode.Should().BeTrue(
            "saving failed: {0}", await response.Content.ReadAsStringAsync());

        var saved = await Context.SubmissionActivityAnswers.AsNoTracking()
            .Where(a => a.ActivityId == new ActivityId(activity.Id))
            .ToListAsync();
        saved.Should().ContainSingle(a =>
            a.QuestionCode == "RNA_INFORMATION_OTHER" && a.Value == "Licensing enforcement data");
    }

    // BLUM-7475: the services question used to be free text, so anything at all saved. Now it is a checkbox list
    // held to the seeded option values, and the comma-joined selection is what persists.
    [Fact]
    public async Task NonDraftSave_WithTheScreeningServicesSelected_SucceedsAndPersistsThem()
    {
        AuthenticateAsMainOrgLead();
        var (collectionId, activity) = await StartConfirmedScreeningReportAsync();

        var response = await SaveAnswersAsync(
            collectionId,
            activity.Id,
            saveAsDraft: false,
            ("SCR_SERVICES", "police,mental_health"),
            ("AKR_DELIVERY_PROGRESS", "Rolled the screening tool out"));

        response.IsSuccessStatusCode.Should().BeTrue(
            "saving failed: {0}", await response.Content.ReadAsStringAsync());

        var saved = await Context.SubmissionActivityAnswers.AsNoTracking()
            .Where(a => a.ActivityId == new ActivityId(activity.Id))
            .ToListAsync();
        saved.Should().ContainSingle(a =>
            a.QuestionCode == "SCR_SERVICES" && a.Value == "police,mental_health");
    }

    [Fact]
    public async Task NonDraftSave_TickingOtherOnTheScreeningServices_RequiresTheFreeText()
    {
        AuthenticateAsMainOrgLead();
        var (collectionId, activity) = await StartConfirmedScreeningReportAsync();

        var response = await SaveAnswersAsync(
            collectionId,
            activity.Id,
            saveAsDraft: false,
            ("SCR_SERVICES", "police,other"),
            ("AKR_DELIVERY_PROGRESS", "Rolled the screening tool out"));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var envelope = await response.ToResponseModel<AnswersInvalidEnvelope>();
        envelope.Error.Should().Be("SUBMISSION_ANSWERS_INVALID");
        envelope.AnswerErrors.Should().ContainSingle(e =>
            e.QuestionCode == "SCR_SERVICES_OTHER"
            && e.Message == "Enter which other services are currently screening for gambling harms");
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