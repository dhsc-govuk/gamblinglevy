using System.Net;
using Api.Dtos;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Tests.Common;
using Xunit;

namespace Api.Tests.Integration.QuarterlyReports;

// The "Provide strategic feedback" section (BLUM-6613): reading the seeded question pages, saving the answers,
// the section status roll-up, and the completion validation (all four questions required).
public class StrategicFeedbackTests(IntegrationTestWebFactory factory) : QuarterlyReportTestBase(factory)
{
    private const string OtherPreventionCode = "SF_OTHER_PREVENTION";
    private const string ChallengesCode = "SF_CHALLENGES";
    private const string DhscSupportCode = "SF_DHSC_SUPPORT";
    private const string LearningsCode = "SF_LEARNINGS";

    private sealed record AnswerErrorRow(string QuestionCode, string Message);

    private sealed record AnswersInvalidEnvelope(string Error, string Message, List<AnswerErrorRow> AnswerErrors);

    private sealed record ErrorEnvelope(string Error, string Message);

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

        feedback.Pages.Select(p => p.Key).Should().Equal("wider-progress", "working-together");
        feedback.Pages.Select(p => p.Title).Should().Equal("Wider progress and challenges", "Working together");

        var questions = feedback.Pages.SelectMany(p => p.Questions).ToList();
        questions.Select(q => q.Code).Should().BeEquivalentTo(
            new[] { OtherPreventionCode, ChallengesCode, DhscSupportCode, LearningsCode });
        questions.Where(q => q.Code != LearningsCode).Should().OnlyContain(q => q.QuestionType == "Textarea");
        var learnings = questions.Single(q => q.Code == LearningsCode);
        learnings.QuestionType.Should().Be("Radio");
        learnings.Hint.Should().Be("If yes, we’ll contact you to find out more");
        learnings.Options.Select(option => (option.Value, option.Label)).Should().Equal(("yes", "Yes"), ("no", "No"));
        questions.Should().OnlyContain(q => q.IsRequired);
        feedback.SectionStatus.Should().Be("not_started");
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
        AuthenticateAsMainOrgLead();
        var collectionId = await StartConfirmedReportAsync();

        // Only one of the four required questions answered.
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