using System.Net;
using Api.Dtos;
using Application.Common.Features;
using Domain.AuditLogs;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Tests.Common;
using Xunit;

namespace Api.Tests.Integration.QuarterlyReports;

// The "Provide strategic feedback" section (BLUM-6613): reading the seeded question pages, saving the answers,
// the section status roll-up, and the completion validation. All four questions, on page 1 ("Wider progress and
// challenges") and page 2 ("Working together"), are optional only while the BLUM-7637 flag (Feature.BLUM_7637) is on
// (page 2 joined in BLUM-9092); with it off every question is required.
// Every flag is off in the suite by default, so each test whose outcome depends on it sets the flag first. When
// the flag is retired, delete the "_WithWiderProgressRequired" tests and the SetWiderProgressOptionalAsync calls.
public class StrategicFeedbackTests(IntegrationTestWebFactory factory) : QuarterlyReportTestBase(factory)
{
    private const string OtherPreventionCode = "SF_OTHER_PREVENTION";
    private const string ChallengesCode = "SF_CHALLENGES";
    private const string DhscSupportCode = "SF_DHSC_SUPPORT";
    private const string LearningsCode = "SF_LEARNINGS";

    private const string WiderProgressPage = "wider-progress";
    private const string WorkingTogetherPage = "working-together";

    private sealed record AnswerErrorRow(string QuestionCode, string Message);

    private sealed record AnswersInvalidEnvelope(string Error, string Message, List<AnswerErrorRow> AnswerErrors);

    private sealed record ErrorEnvelope(string Error, string Message);

    // The restart that applies the flag in a deployed environment re-runs the seed, and so does this.
    private Task SetWiderProgressOptionalAsync(bool optional) =>
        factory.SetFeatureAsync(Feature.BLUM_7637, optional);

    private async Task<Guid> StartConfirmedReportAsync()
    {
        var collectionId = await CreateOpenCollectionAsync();
        await CreateRnaActivityAsync();
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);
        return collectionId;
    }

    [Fact]
    public async Task GetStrategicFeedback_ReturnsTheDesignedQuestionTypesAndCopy()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await StartConfirmedReportAsync();

        var feedback = await GetStrategicFeedbackAsync(collectionId);

        feedback.Pages.Select(p => p.Key).Should().Equal(WiderProgressPage, WorkingTogetherPage);
        feedback.Pages.Select(p => p.Title).Should().Equal("Wider progress and challenges", "Working together");

        var questions = feedback.Pages.SelectMany(p => p.Questions).ToList();
        questions.Select(q => q.Code).Should().BeEquivalentTo(
            new[] { OtherPreventionCode, ChallengesCode, DhscSupportCode, LearningsCode });
        questions.Where(q => q.Code != LearningsCode).Should().OnlyContain(q => q.QuestionType == "Textarea");
        var learnings = questions.Single(q => q.Code == LearningsCode);
        learnings.QuestionType.Should().Be("Radio");
        learnings.Hint.Should().Be("If yes, we’ll contact you to find out more");
        learnings.Options.Select(option => (option.Value, option.Label)).Should().Equal(("yes", "Yes"), ("no", "No"));
        feedback.SectionStatus.Should().Be("not_started");
    }

    [Fact]
    public async Task GetStrategicFeedback_MarksEveryQuestionOptional()
    {
        await SetWiderProgressOptionalAsync(true);
        AuthenticateAsMainOrgLead();
        var collectionId = await StartConfirmedReportAsync();

        var feedback = await GetStrategicFeedbackAsync(collectionId);

        var widerProgress = feedback.Pages.Single(p => p.Key == WiderProgressPage).Questions;
        widerProgress.Select(q => q.Code).Should().Equal(OtherPreventionCode, ChallengesCode);
        widerProgress.Should().OnlyContain(q => !q.IsRequired);
        widerProgress.Select(q => q.Label).Should().Equal(
            "What other gambling harms prevention work have you focused on this quarter? (optional)",
            "What challenges have you encountered? (optional)");

        var workingTogether = feedback.Pages.Single(p => p.Key == WorkingTogetherPage).Questions;
        workingTogether.Select(q => q.Code).Should().Equal(DhscSupportCode, LearningsCode);
        workingTogether.Should().OnlyContain(q => !q.IsRequired);
        workingTogether.Select(q => q.Label).Should().Equal(
            "Do you have any feedback for the Department of Health and Social Care (DHSC) on gambling harms prevention? (optional)",
            "Do you have any learnings you’d like to share with other local authorities? (optional)");
    }

    [Fact]
    public async Task SaveDraft_PersistsAnswersAndSetsInProgress()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await StartConfirmedReportAsync();

        var response = await SaveStrategicFeedbackAsync(collectionId, saveAsDraft: true, (OtherPreventionCode, "Community outreach work"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var state = await response.ToResponseModel<StrategicFeedbackStateDto>();
        state.SectionStatus.Should().Be("in_progress");

        var saved = await Context.SubmissionFeedbackAnswers.AsNoTracking().ToListAsync();
        saved.Should().ContainSingle(a => a.QuestionCode == OtherPreventionCode && a.Value == "Community outreach work");

        var feedback = await GetStrategicFeedbackAsync(collectionId);
        feedback.Answers.Should().ContainSingle(a => a.QuestionCode == OtherPreventionCode && a.Value == "Community outreach work");

        var taskList = await GetTaskListAsync(collectionId);
        taskList.Sections.Single(s => s.ModuleCode == FeedbackModuleCode).Status.Should().Be("in_progress");
    }

    // BLUM-8747: a draft save is audited, but as counts only. The feedback is free text an LA wrote, and the audit log
    // is readable by every member of that LA (and DHSC), so the answer must never be copied into the row.
    [Fact]
    public async Task SaveDraft_WritesOneSavedAuditRow_WithoutTheAnswerText()
    {
        const string distinctiveAnswer = "Zebra-crossing outreach 8747 never-audit-me";
        AuthenticateAsMainOrgLead();
        var collectionId = await StartConfirmedReportAsync();

        var response = await SaveStrategicFeedbackAsync(collectionId, saveAsDraft: true, (OtherPreventionCode, distinctiveAnswer));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var submissionId = await GetSubmissionIdAsync(collectionId);
        var rows = await Context.AuditLogs.AsNoTracking()
            .Where(a => a.Action == AuditAction.SubmissionStrategicFeedbackSaved)
            .ToListAsync();
        var row = rows.Should().ContainSingle().Subject;
        row.TargetType.Should().Be(AuditTargetType.Submission);
        row.TargetId.Should().Be(submissionId.Value);
        row.OrganisationId.Should().Be(MainOrg.Id);
        row.After.Should().NotBeNull();
        row.After.Should().NotContain(distinctiveAnswer).And.NotContain("Zebra");
        row.Before.Should().BeNull();
    }

    [Fact]
    public async Task SaveDraft_WithNoAnswers_StaysNotStarted()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await StartConfirmedReportAsync();

        var response = await SaveStrategicFeedbackAsync(collectionId, saveAsDraft: true);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var state = await response.ToResponseModel<StrategicFeedbackStateDto>();
        state.SectionStatus.Should().Be("not_started");
        (await Context.SubmissionFeedbackAnswers.AsNoTracking().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task SaveDraft_SavingOnePage_DoesNotWipeTheOtherPagesAnswers()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await StartConfirmedReportAsync();

        // Page 1 ("Wider progress and challenges") saved first.
        (await SaveStrategicFeedbackAsync(collectionId, saveAsDraft: true, (OtherPreventionCode, "Outreach"), (ChallengesCode, "Recruitment"))).EnsureSuccessStatusCode();
        // Page 2 ("Working together") saved next — must not wipe page 1's answers.
        (await SaveStrategicFeedbackAsync(collectionId, saveAsDraft: true, (DhscSupportCode, "More funding"), (LearningsCode, "yes"))).EnsureSuccessStatusCode();

        var feedback = await GetStrategicFeedbackAsync(collectionId);
        feedback.Answers.Should().HaveCount(4);
        feedback.Answers.Should().Contain(a => a.QuestionCode == OtherPreventionCode && a.Value == "Outreach");
        feedback.Answers.Should().Contain(a => a.QuestionCode == ChallengesCode && a.Value == "Recruitment");
        feedback.Answers.Should().Contain(a => a.QuestionCode == DhscSupportCode && a.Value == "More funding");
        feedback.Answers.Should().Contain(a => a.QuestionCode == LearningsCode && a.Value == "yes");
    }

    [Fact]
    public async Task SaveDraft_ReSavingAPage_ReplacesOnlyThatPagesAnswersAndClearsBlanks()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await StartConfirmedReportAsync();

        (await SaveStrategicFeedbackAsync(collectionId, saveAsDraft: true, (OtherPreventionCode, "First"), (ChallengesCode, "Second"))).EnsureSuccessStatusCode();
        // Re-save page 1 with the second answer cleared — only page 1's codes are replaced, the blank is removed.
        (await SaveStrategicFeedbackAsync(collectionId, saveAsDraft: true, (OtherPreventionCode, "Updated"), (ChallengesCode, ""))).EnsureSuccessStatusCode();

        var saved = await Context.SubmissionFeedbackAnswers.AsNoTracking().ToListAsync();
        saved.Should().ContainSingle();
        saved.Single().QuestionCode.Should().Be(OtherPreventionCode);
        saved.Single().Value.Should().Be("Updated");
    }

    [Fact]
    public async Task Complete_WithMissingAnswers_Returns422AndDoesNotComplete()
    {
        await SetWiderProgressOptionalAsync(false);
        AuthenticateAsMainOrgLead();
        var collectionId = await StartConfirmedReportAsync();

        // With the flag off every question is required: one answered, the other three refused.
        (await SaveStrategicFeedbackAsync(collectionId, saveAsDraft: true, (OtherPreventionCode, "Some work")))
            .EnsureSuccessStatusCode();

        var response = await CompleteStrategicFeedbackAsync(collectionId);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var envelope = await response.ToResponseModel<AnswersInvalidEnvelope>();
        envelope.Error.Should().Be("SUBMISSION_ANSWERS_INVALID");
        envelope.AnswerErrors.Select(e => e.QuestionCode).Should().BeEquivalentTo(
            new[] { ChallengesCode, DhscSupportCode, LearningsCode });

        var taskList = await GetTaskListAsync(collectionId);
        taskList.Sections.Single(s => s.ModuleCode == FeedbackModuleCode).Status.Should().NotBe("completed");
    }

    // BLUM-7637 AC1: leaving "Wider progress and challenges" blank does not block completing the section.
    [Fact]
    public async Task Complete_WithWiderProgressLeftBlank_FlipsTheSectionToCompleted()
    {
        await SetWiderProgressOptionalAsync(true);
        AuthenticateAsMainOrgLead();
        var collectionId = await StartConfirmedReportAsync();

        (await SaveStrategicFeedbackAsync(collectionId, saveAsDraft: true, (DhscSupportCode, "More funding"), (LearningsCode, "no")))
            .EnsureSuccessStatusCode();

        var response = await CompleteStrategicFeedbackAsync(collectionId);

        response.IsSuccessStatusCode.Should().BeTrue("completing failed: {0}", await response.Content.ReadAsStringAsync());

        var feedback = await GetStrategicFeedbackAsync(collectionId);
        feedback.SectionStatus.Should().Be("completed");
        feedback.Answers.Select(a => a.QuestionCode).Should().BeEquivalentTo(new[] { DhscSupportCode, LearningsCode });

        var taskList = await GetTaskListAsync(collectionId);
        taskList.Sections.Single(s => s.ModuleCode == FeedbackModuleCode).Status.Should().Be("completed");
    }

    // BLUM-9092 (BLUM-6427 AC1): with nothing answered on either page the section still completes.
    [Fact]
    public async Task Complete_WithNothingAnswered_FlipsTheSectionToCompleted()
    {
        await SetWiderProgressOptionalAsync(true);
        AuthenticateAsMainOrgLead();
        var collectionId = await StartConfirmedReportAsync();

        var response = await CompleteStrategicFeedbackAsync(collectionId);

        response.IsSuccessStatusCode.Should().BeTrue("completing failed: {0}", await response.Content.ReadAsStringAsync());
        (await GetStrategicFeedbackAsync(collectionId)).SectionStatus.Should().Be("completed");

        var taskList = await GetTaskListAsync(collectionId);
        taskList.Sections.Single(s => s.ModuleCode == FeedbackModuleCode).Status.Should().Be("completed");
    }

    // BLUM-9092: a report whose strategic feedback is entirely blank can be submitted.
    [Fact]
    public async Task SubmitReport_WithEveryFeedbackQuestionLeftBlank_Succeeds()
    {
        await SetWiderProgressOptionalAsync(true);
        AuthenticateAsMainOrgLead();
        var collectionId = await CreateOpenCollectionAsync();
        var activity = await CreateRnaActivityAsync();
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);
        await CompleteActivityReportAsync(collectionId, activity.Id);
        await CompleteFundingSpendSectionAsync(collectionId);
        (await CompleteStrategicFeedbackAsync(collectionId)).EnsureSuccessStatusCode();

        var response = await SubmitReportAsync(collectionId);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var submissionId = await GetSubmissionIdAsync(collectionId);
        (await Context.SubmissionFeedbackAnswers.AsNoTracking().AnyAsync(a => a.SubmissionId == submissionId)).Should().BeFalse();
    }

    // BLUM-7637 AC2: a report whose "Wider progress and challenges" page is empty can be submitted.
    [Fact]
    public async Task SubmitReport_WithWiderProgressLeftBlank_Succeeds()
    {
        await SetWiderProgressOptionalAsync(true);
        AuthenticateAsMainOrgLead();
        var collectionId = await CreateOpenCollectionAsync();
        var activity = await CreateRnaActivityAsync();
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);
        await CompleteActivityReportAsync(collectionId, activity.Id);
        await CompleteFundingSpendSectionAsync(collectionId);

        (await SaveStrategicFeedbackAsync(collectionId, saveAsDraft: true, (DhscSupportCode, "More funding"), (LearningsCode, "yes")))
            .EnsureSuccessStatusCode();
        (await CompleteStrategicFeedbackAsync(collectionId)).EnsureSuccessStatusCode();

        var response = await SubmitReportAsync(collectionId);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var submissionId = await GetSubmissionIdAsync(collectionId);
        var submission = await Context.Submissions.AsNoTracking().SingleAsync(s => s.Id == submissionId);
        submission.SubmittedAt.Should().NotBeNull();

        var answers = await Context.SubmissionFeedbackAnswers.AsNoTracking()
            .Where(a => a.SubmissionId == submissionId)
            .Select(a => a.QuestionCode)
            .ToListAsync();
        answers.Should().BeEquivalentTo(new[] { DhscSupportCode, LearningsCode });
    }

    // BLUM-7637 AC3, and what releasing the flag does to a deployed database: its page-1 rows are required, and it is
    // the seeder's every-boot reconcile — not a migration — that relaxes them on the restart that switches the flag
    // on. Answers already saved are left untouched.
    [Fact]
    public async Task SwitchingTheFlagOn_MakesWiderProgressOptionalAndKeepsSavedAnswers()
    {
        await SetWiderProgressOptionalAsync(false);
        AuthenticateAsMainOrgLead();
        var collectionId = await StartConfirmedReportAsync();
        (await SaveStrategicFeedbackAsync(collectionId, saveAsDraft: true, (OtherPreventionCode, "Outreach"), (ChallengesCode, "Recruitment")))
            .EnsureSuccessStatusCode();
        (await GetStrategicFeedbackAsync(collectionId)).Pages.Single(p => p.Key == WiderProgressPage).Questions
            .Should().OnlyContain(q => q.IsRequired);

        await SetWiderProgressOptionalAsync(true);

        var widerProgress = (await GetStrategicFeedbackAsync(collectionId)).Pages.Single(p => p.Key == WiderProgressPage).Questions;
        widerProgress.Should().OnlyContain(q => !q.IsRequired && q.Label.EndsWith(" (optional)"));
        (await SavedAnswersAsync(collectionId)).Should().BeEquivalentTo(
            new[] { (OtherPreventionCode, "Outreach"), (ChallengesCode, "Recruitment") });
    }

    // The way back: switching the flag off again (a rollback, or DAC never setting it) converges the rows to required
    // on the next boot. Nothing is deleted — the answers an LA gave while page 1 was optional stay saved.
    [Fact]
    public async Task SwitchingTheFlagOff_MakesWiderProgressRequiredAgainAndKeepsSavedAnswers()
    {
        await SetWiderProgressOptionalAsync(true);
        AuthenticateAsMainOrgLead();
        var collectionId = await StartConfirmedReportAsync();
        (await SaveStrategicFeedbackAsync(collectionId, saveAsDraft: true, (OtherPreventionCode, "Outreach"), (ChallengesCode, "Recruitment")))
            .EnsureSuccessStatusCode();

        await SetWiderProgressOptionalAsync(false);

        var widerProgress = (await GetStrategicFeedbackAsync(collectionId)).Pages.Single(p => p.Key == WiderProgressPage).Questions;
        widerProgress.Should().OnlyContain(q => q.IsRequired && !q.Label.Contains("(optional)"));
        (await SavedAnswersAsync(collectionId)).Should().BeEquivalentTo(
            new[] { (OtherPreventionCode, "Outreach"), (ChallengesCode, "Recruitment") });
    }

    [Fact]
    public async Task GetStrategicFeedback_WithWiderProgressRequired_MarksEveryQuestionRequiredWithoutTheOptionalSuffix()
    {
        await SetWiderProgressOptionalAsync(false);
        AuthenticateAsMainOrgLead();
        var collectionId = await StartConfirmedReportAsync();

        var feedback = await GetStrategicFeedbackAsync(collectionId);

        var questions = feedback.Pages.SelectMany(p => p.Questions).ToList();
        questions.Should().OnlyContain(q => q.IsRequired && !q.Label.Contains("(optional)"));
        feedback.Pages.Single(p => p.Key == WiderProgressPage).Questions.Select(q => q.Label).Should().Equal(
            "What other gambling harms prevention work have you focused on this quarter?",
            "What challenges have you encountered?");
    }

    [Fact]
    public async Task Complete_WithWiderProgressRequired_AndWiderProgressLeftBlank_Returns422ForTheWiderProgressQuestions()
    {
        await SetWiderProgressOptionalAsync(false);
        AuthenticateAsMainOrgLead();
        var collectionId = await StartConfirmedReportAsync();
        (await SaveStrategicFeedbackAsync(collectionId, saveAsDraft: true, (DhscSupportCode, "More funding"), (LearningsCode, "no")))
            .EnsureSuccessStatusCode();

        var response = await CompleteStrategicFeedbackAsync(collectionId);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var envelope = await response.ToResponseModel<AnswersInvalidEnvelope>();
        envelope.Error.Should().Be("SUBMISSION_ANSWERS_INVALID");
        envelope.AnswerErrors.Select(e => e.QuestionCode).Should().BeEquivalentTo(new[] { OtherPreventionCode, ChallengesCode });

        var taskList = await GetTaskListAsync(collectionId);
        taskList.Sections.Single(s => s.ModuleCode == FeedbackModuleCode).Status.Should().NotBe("completed");
    }

    // What DHSC needs to know before switching the flag off mid-quarter: a section an LA completed with page 1 blank
    // is not re-opened, and the report still submits, because SubmitReportCommand checks section status rather than
    // re-validating the answers. Only saving the section again would make page 1 required for that LA.
    [Fact]
    public async Task SwitchingTheFlagOff_LeavesASectionCompletedWithWiderProgressBlankSubmittable()
    {
        await SetWiderProgressOptionalAsync(true);
        AuthenticateAsMainOrgLead();
        var collectionId = await CreateOpenCollectionAsync();
        var activity = await CreateRnaActivityAsync();
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);
        await CompleteActivityReportAsync(collectionId, activity.Id);
        await CompleteFundingSpendSectionAsync(collectionId);
        (await SaveStrategicFeedbackAsync(collectionId, saveAsDraft: true, (DhscSupportCode, "More funding"), (LearningsCode, "yes")))
            .EnsureSuccessStatusCode();
        (await CompleteStrategicFeedbackAsync(collectionId)).EnsureSuccessStatusCode();

        await SetWiderProgressOptionalAsync(false);

        var taskList = await GetTaskListAsync(collectionId);
        taskList.Sections.Single(s => s.ModuleCode == FeedbackModuleCode).Status.Should().Be("completed");
        var response = await SubmitReportAsync(collectionId);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private async Task<List<(string QuestionCode, string Value)>> SavedAnswersAsync(Guid collectionId)
    {
        var submissionId = await GetSubmissionIdAsync(collectionId);
        var saved = await Context.SubmissionFeedbackAnswers.AsNoTracking().Where(a => a.SubmissionId == submissionId).ToListAsync();
        return saved.Select(a => (a.QuestionCode, a.Value)).ToList();
    }

    [Fact]
    public async Task Complete_WithEverythingAnswered_FlipsTheSectionToCompleted()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await StartConfirmedReportAsync();

        await CompleteStrategicFeedbackSectionAsync(collectionId);

        var feedback = await GetStrategicFeedbackAsync(collectionId);
        feedback.SectionStatus.Should().Be("completed");

        var taskList = await GetTaskListAsync(collectionId);
        taskList.Sections.Single(s => s.ModuleCode == FeedbackModuleCode).Status.Should().Be("completed");
    }

    [Fact]
    public async Task SaveDraft_AfterCompleting_DropsTheSectionBackToInProgress()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await StartConfirmedReportAsync();
        await CompleteStrategicFeedbackSectionAsync(collectionId);

        (await SaveStrategicFeedbackAsync(collectionId, saveAsDraft: true, (OtherPreventionCode, "Edited")))
            .EnsureSuccessStatusCode();

        var taskList = await GetTaskListAsync(collectionId);
        taskList.Sections.Single(s => s.ModuleCode == FeedbackModuleCode).Status.Should().Be("in_progress");
    }

    [Fact]
    public async Task Save_BeforeConfirmingActivities_Returns422ActivitiesNotConfirmed()
    {
        var collectionId = await CreateOpenCollectionAsync();
        AuthenticateAsMainOrgLead();
        await CreateRnaActivityAsync();
        await CreateSubmissionAsync(collectionId);

        var response = await SaveStrategicFeedbackAsync(collectionId, saveAsDraft: true, (OtherPreventionCode, "x"));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var envelope = await response.ToResponseModel<ErrorEnvelope>();
        envelope.Error.Should().Be("ACTIVITIES_NOT_CONFIRMED");
    }

    [Fact]
    public async Task Save_ForACollectionAssignedToAnotherOrg_Returns404()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await StartConfirmedReportAsync();

        AuthenticateAsSecondOrgLead();
        var response = await SaveStrategicFeedbackAsync(collectionId, saveAsDraft: true, (OtherPreventionCode, "x"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}