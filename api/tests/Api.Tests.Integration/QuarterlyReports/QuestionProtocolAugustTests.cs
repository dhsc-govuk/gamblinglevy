using System.Net;
using Api.Dtos;
using Domain.Activities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Tests.Common;
using Xunit;

namespace Api.Tests.Integration.QuarterlyReports;

// The 13 August Question Protocol's blue-highlighted changes to the quarterly report (BLUM-7620): the new
// results-table questions and field types, the free-text character limits on the delivery and strategic-feedback
// pages, and the reworded funding-spend money rules.
public class QuestionProtocolAugustTests(IntegrationTestWebFactory factory) : QuarterlyReportTestBase(factory)
{
    private const string AdvertisingTaskName = "Restricting gambling advertising on council-owned spaces";
    private const string StrategyTaskName = "Documenting a strategy and action plan";

    private sealed record AnswerErrorRow(string QuestionCode, string Message);

    private sealed record AnswersInvalidEnvelope(string Error, string Message, List<AnswerErrorRow> AnswerErrors);

    // An open collection, one confirmed Active activity of the given type with the given standard key results,
    // and the questions it unlocks. The caller must already be authenticated as a Main-org activity manager.
    private Task<(Guid CollectionId, ActivityDto Activity, ActivityReportQuestionsDto Form)> StartReportForAsync(
        string taskName, params string[] keyResultCodes) =>
        StartReportAtStatusAsync(taskName, "active", keyResultCodes);

    // As above at a chosen activity status. A distinct NAME rather than an overload: the status cannot be an
    // optional parameter in front of `params`, and an overload taking (taskName, status, params) silently steals
    // every two-argument call — applicable in normal form with an empty params array, which beats the other
    // overload's expanded form, so StartReportForAsync(task, "adv_restrictions") would read the measure as a status.
    private async Task<(Guid CollectionId, ActivityDto Activity, ActivityReportQuestionsDto Form)> StartReportAtStatusAsync(
        string taskName, string status, params string[] keyResultCodes)
    {
        var collectionId = await CreateOpenCollectionAsync();
        var (_, taskId) = await GetTypeAsync(taskName);
        var results = await StandardResultsAsync(taskId, keyResultCodes);
        var activity = await CreateCompletedActivityAsync(taskName, status, results);
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);
        return (collectionId, activity, await GetActivityQuestionsAsync(collectionId, activity.Id));
    }

    // The report sections only unlock once the activity list is confirmed, so even the feedback tests need one
    // reportable activity behind them.
    private async Task<Guid> StartConfirmedReportAsync()
    {
        var collectionId = await CreateOpenCollectionAsync();
        await CreateRnaActivityAsync();
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);
        return collectionId;
    }

    // --- Results table: the new questions and field types ---

    // Sheet 1.D.1D (13 August): the advertising restriction stops being a yes/no because a partial restriction is
    // now a distinct answer. It is the protocol's first Radio field type — Boolean is the only choice type whose
    // options the resolver synthesises, so a Radio that lost its seeded options would render an empty control.
    [Fact]
    public async Task ShouldAskTheAdvertisingRestriction_AsAThreeOptionRadio()
    {
        AuthenticateAsMainOrgLead();
        var (_, _, form) = await StartReportForAsync(AdvertisingTaskName, "adv_restrictions");

        var question = form.Questions.Should().ContainSingle(q => q.Code == "ADV_RESTRICTIONS").Subject;
        question.Label.Should().Be("Has your local authority restricted gambling advertising on council-owned property?");
        question.QuestionType.Should().Be("Radio");
        question.Options.Select(o => o.Label).Should().Equal(
            "Yes, on all council-owned property", "Yes, on some property", "No");
    }

    [Fact]
    public async Task ShouldRejectAnAdvertisingAnswer_ThatIsNotOneOfTheSeededOptions()
    {
        AuthenticateAsMainOrgLead();
        var (collectionId, activity, _) = await StartReportForAsync(AdvertisingTaskName, "adv_restrictions");

        var response = await SaveAnswersAsync(collectionId, activity.Id, saveAsDraft: true, ("ADV_RESTRICTIONS", "yes"));

        response.StatusCode.Should().Be(
            HttpStatusCode.UnprocessableEntity,
            "\"yes\" was valid while this was a Boolean and must not survive the change to a three-way answer");
        (await response.ToResponseModel<AnswersInvalidEnvelope>()).AnswerErrors
            .Should().Contain(e => e.QuestionCode == "ADV_RESTRICTIONS");
    }

    // Sheet 1.C.1D (13 August): the engagement question the 3 July sheet struck through is back. It arrived
    // alongside a "Who attended each meeting?" question on the "Meeting attendance" key result, which BLUM-7790
    // then withdrew together with the result itself.
    //
    // The measures are read from the taxonomy rather than named literally, and that is load-bearing for the last
    // assertion: KeyResultQuestionResolver only emits questions for key results the activity actually SELECTED, so
    // a hardcoded "lsg_members" would make the LSG_ATTENDANCE check unfalsifiable — restoring both seed rows would
    // leave the attendance question keyed to a result this activity simply never picked. Selecting everything the
    // type offers is what makes the withdrawal testable from the report side.
    [Fact]
    public async Task ShouldAskTheSteeringGroupEngagementQuestion_OncePerMember_AndNotAttendance()
    {
        AuthenticateAsMainOrgLead();
        var (_, taskId) = await GetTypeAsync(SteeringGroupTaskName);
        var everyMeasure = (await GetTaxonomyAsync()).KeyResults
            .Where(k => k.TaskId == taskId).Select(k => k.Code).ToArray();

        var (_, _, form) = await StartReportForAsync(SteeringGroupTaskName, everyMeasure);

        // BLUM-7788: the engagement question is asked once per member the steering-group list offers, each revealed
        // by that member being ticked — so the form carries one question per LSG_MEMBERS option, and the single
        // LSG_ENGAGEMENT radio BLUM-7620 substituted for the protocol's multi-row control is gone.
        var members = form.Questions.Should().ContainSingle(q => q.Code == "LSG_MEMBERS").Subject;
        var engagement = form.Questions.Where(q => q.Code.StartsWith("LSG_ENGAGEMENT_", StringComparison.Ordinal)).ToList();

        engagement.Select(q => q.ConditionalValue).Should().Equal(
            members.Options.Select(o => (string?)o.Value),
            "each member of the steering-group list gets its own engagement question");
        form.Questions.Should().NotContain(q => q.Code == "LSG_ENGAGEMENT");

        var licensing = engagement.Should().ContainSingle(q => q.ConditionalValue == "licensing").Subject;
        licensing.Code.Should().Be("LSG_ENGAGEMENT_LICENSING");
        licensing.Label.Should().Be("What is their level of engagement? (Licensing)");
        licensing.QuestionType.Should().Be("Radio");
        licensing.IsRequired.Should().BeTrue();
        licensing.ConditionalQuestionCode.Should().Be("LSG_MEMBERS");
        licensing.ErrorMessage.Should().Be("Select the level of engagement of Licensing");
        licensing.Options.Select(o => o.Label).Should().Equal(
            "Attends and engages", "Attends but with limited engagement", "Contacted but no response", "No engagement", "Do not know");

        engagement.Should().OnlyContain(
            q => q.Options.Count == 5, "every member's question offers the same five engagement levels");

        form.Questions.Should().NotContain(
            q => q.Code == "LSG_ATTENDANCE",
            "BLUM-7790 withdrew the 'Who attended each meeting?' question, so no measure this type offers unlocks it");
    }

    // The measures screen must no longer offer "Meeting attendance" either — removing only the question would
    // leave a selectable result that unlocks nothing.
    [Fact]
    public async Task ShouldNoLongerOfferTheMeetingAttendanceKeyResult()
    {
        AuthenticateAsMainOrgLead();
        var (_, taskId) = await GetTypeAsync(SteeringGroupTaskName);
        var taxonomy = await GetTaxonomyAsync();

        taxonomy.KeyResults.Where(k => k.TaskId == taskId).Select(k => k.Code).Should().Equal("lsg_members");
    }

    // Sheet 1.C.1C (13 August) separates "[A] Strategy and action plan developed" from "[B] Work on gambling harms
    // presented to the Health and Wellbeing Board"; the 3 July transcription folded both onto one key result, which
    // left the drafting question hanging off a result about the Board.
    [Fact]
    public async Task ShouldSplitTheStrategyKeyResults_IntoPlanAndBoardPresentation()
    {
        AuthenticateAsMainOrgLead();
        var (_, taskId) = await GetTypeAsync(StrategyTaskName);
        var taxonomy = await GetTaxonomyAsync();

        var codes = taxonomy.KeyResults.Where(k => k.TaskId == taskId).Select(k => k.Code).ToList();
        codes.Should().ContainInOrder("sap_plan_developed", "sap_hwb_presented", "sap_other_strategies");
        taxonomy.KeyResults.Single(k => k.TaskId == taskId && k.Code == "sap_hwb_presented").Label
            .Should().Be("Work on gambling harms presented to the Health and Wellbeing Board");
    }

    [Fact]
    public async Task ShouldAskBothStrategyQuestions_ForAnActiveActivity()
    {
        AuthenticateAsMainOrgLead();
        var (_, _, form) = await StartReportForAsync(StrategyTaskName, "sap_plan_developed", "sap_hwb_presented");

        form.Questions.Should().ContainSingle(q => q.Code == "SAP_PLAN_DEVELOPED")
            .Which.Label.Should().Be("Is a draft strategy and/or action plan being developed?");
        form.Questions.Should().ContainSingle(q => q.Code == "SAP_HWB_PRESENTED")
            .Which.Label.Should().Be("Will you present work on gambling harms prevention to the Health and Wellbeing Board?");
    }

    // --- BLUM-7791: the Health and Wellbeing Board pair ---
    //
    // Both phrasings share the SAP_HWB_PRESENTED code and differ only by StatusScope, so ContainSingle is what
    // makes "only one of them" falsifiable: a broken status filter shows up here as two questions, not as the
    // wrong label. The rules held from BLUM-7620 onwards but were true only by construction — these three facts
    // and their unit counterparts are what stops the next re-transcription undoing them silently.

    [Fact]
    public async Task ShouldAskOnlyTheFutureTenseBoardQuestion_WhileTheActivityIsActive()
    {
        AuthenticateAsMainOrgLead();
        var (_, _, form) = await StartReportAtStatusAsync(StrategyTaskName, "active", "sap_hwb_presented");

        var board = form.Questions.Should().ContainSingle(q => q.Code == "SAP_HWB_PRESENTED").Subject;
        board.Label.Should().Be("Will you present work on gambling harms prevention to the Health and Wellbeing Board?");
        board.IsRequired.Should().BeTrue("the protocol marks the row Conditional — shown means required");
        board.ErrorMessage.Should().Be("Select yes if you are planning on presenting to the Health and Wellbeing Board");

        // Boolean is the one choice type whose options the resolver synthesises, and neither is preselected —
        // the protocol's error trigger for this row is "nothing selected".
        board.QuestionType.Should().Be("Radio");
        board.Options.Select(o => o.Value).Should().Equal("yes", "no");
        form.Answers.Should().NotContain(a => a.QuestionCode == "SAP_HWB_PRESENTED");
    }

    [Theory]
    [InlineData("closed")]
    [InlineData("finished")]
    public async Task ShouldAskOnlyThePastTenseBoardQuestion_OnceTheActivityIsClosedOrFinished(string status)
    {
        AuthenticateAsMainOrgLead();
        var (_, _, form) = await StartReportAtStatusAsync(StrategyTaskName, status, "sap_plan_developed", "sap_hwb_presented");

        var board = form.Questions.Should().ContainSingle(q => q.Code == "SAP_HWB_PRESENTED").Subject;
        board.Label.Should().Be("Have you presented work on gambling harms prevention to the Health and Wellbeing Board?");
        board.IsRequired.Should().BeTrue();
        board.ErrorMessage.Should().Be("Select yes if you have presented to the Health and Wellbeing Board");
        board.QuestionType.Should().Be("Radio");

        // Same revision, same screen: DHSC answered the "presented" / "developed" contradiction BLUM-7620 raised
        // against the sibling result's Closed/Finished error.
        form.Questions.Should().ContainSingle(q => q.Code == "SAP_PLAN_DEVELOPED")
            .Which.ErrorMessage.Should().Be("Select yes if a draft has been developed");
    }

    [Theory]
    [InlineData("active")]
    [InlineData("closed")]
    [InlineData("finished")]
    public async Task ShouldAskNeitherBoardQuestion_WhenTheBoardMeasureWasNotSelected(string status)
    {
        AuthenticateAsMainOrgLead();
        var (_, _, form) = await StartReportAtStatusAsync(StrategyTaskName, status, "sap_plan_developed");

        form.Questions.Should().NotContain(
            q => q.Code == "SAP_HWB_PRESENTED",
            "the Board questions hang off the sap_hwb_presented measure, which this activity did not select");
        form.Questions.Should().Contain(
            q => q.Code == "SAP_PLAN_DEVELOPED",
            "the measure it did select is still asked, so an empty result set could not pass this test");
    }

    // "Shown and required" on the wire, not just in the DTO: completing with nothing selected must refuse with the
    // protocol's own wording. The client renders exactly this string, so a drift here is a drift on screen.
    [Fact]
    public async Task ShouldRefuseToCompleteTheBoardQuestion_WithTheProtocolsErrorMessage()
    {
        AuthenticateAsMainOrgLead();
        var (collectionId, activity, _) = await StartReportAtStatusAsync(StrategyTaskName, "active", "sap_hwb_presented");

        var response = await SaveAnswersAsync(collectionId, activity.Id, saveAsDraft: false, ("SAP_HWB_PRESENTED", null));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ToResponseModel<AnswersInvalidEnvelope>()).AnswerErrors.Should().Contain(
            e => e.QuestionCode == "SAP_HWB_PRESENTED"
                && e.Message == "Select yes if you are planning on presenting to the Health and Wellbeing Board");
    }

    // --- BLUM-8159: "Other" on the other-strategies list ---
    //
    // The data map closes "Which other strategies have been updated to include gambling harms?" with an "Other"
    // the seed lacked. Like every protocol "Other" since BLUM-7472, ticking it reveals a free-text companion.

    [Theory]
    [InlineData("closed")]
    [InlineData("finished")]
    public async Task ShouldOfferOther_OnTheOtherStrategiesQuestion_WithAFreeTextCompanion(string status)
    {
        AuthenticateAsMainOrgLead();
        var (_, _, form) = await StartReportAtStatusAsync(StrategyTaskName, status, "sap_other_strategies");

        var strategies = form.Questions.Should().ContainSingle(q => q.Code == "SAP_OTHER_STRATEGIES").Subject;
        strategies.Options.Select(o => o.Value).Should().Equal("alcohol", "drugs", "mental_health", "suicide", "other");
        strategies.Options[^1].Label.Should().Be("Other");

        var companion = form.Questions.Should().ContainSingle(q => q.Code == "SAP_OTHER_STRATEGIES_OTHER").Subject;
        companion.Label.Should().Be("List the other strategies that have been updated to include gambling harms");
        companion.ConditionalQuestionCode.Should().Be("SAP_OTHER_STRATEGIES");
        companion.ConditionalValue.Should().Be("other");
        companion.IsRequired.Should().BeTrue("a revealed companion is required, or \"Other\" is left unexplained");
        companion.ErrorMessage.Should().Be("Enter the other strategies that have been updated");
    }

    // Before this change the option allow-list refused "other" outright; now it must be accepted and its free text
    // kept alongside it.
    [Fact]
    public async Task ShouldSaveOtherStrategies_WithTheOtherFreeText()
    {
        AuthenticateAsMainOrgLead();
        var (collectionId, activity, _) = await StartReportAtStatusAsync(StrategyTaskName, "finished", "sap_other_strategies");

        var response = await SaveAnswersAsync(
            collectionId,
            activity.Id,
            saveAsDraft: true,
            ("SAP_OTHER_STRATEGIES", "alcohol,other"),
            ("SAP_OTHER_STRATEGIES_OTHER", "Physical activity strategy"));

        response.IsSuccessStatusCode.Should().BeTrue("saving failed: {0}", await response.Content.ReadAsStringAsync());

        var saved = await Context.SubmissionActivityAnswers.AsNoTracking()
            .Where(a => a.ActivityId == new ActivityId(activity.Id))
            .ToListAsync();
        saved.Should().ContainSingle(a => a.QuestionCode == "SAP_OTHER_STRATEGIES" && a.Value == "alcohol,other");
        saved.Should().ContainSingle(a => a.QuestionCode == "SAP_OTHER_STRATEGIES_OTHER" && a.Value == "Physical activity strategy");
    }

    [Fact]
    public async Task ShouldRefuseToCompleteOtherStrategies_WhenOtherIsTickedWithoutTheFreeText()
    {
        AuthenticateAsMainOrgLead();
        var (collectionId, activity, _) = await StartReportAtStatusAsync(StrategyTaskName, "finished", "sap_other_strategies");

        var response = await SaveAnswersAsync(
            collectionId, activity.Id, saveAsDraft: false, ("SAP_OTHER_STRATEGIES", "other"), ("SAP_OTHER_STRATEGIES_OTHER", null));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ToResponseModel<AnswersInvalidEnvelope>()).AnswerErrors.Should().Contain(
            e => e.QuestionCode == "SAP_OTHER_STRATEGIES_OTHER" && e.Message == "Enter the other strategies that have been updated");
    }

    // Sheet rows for every "[X] Other" result: the synthesised question for a custom measure now names it.
    [Fact]
    public async Task ShouldNameTheMeasure_InACustomResultsQuestionAndItsError()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await CreateOpenCollectionAsync();
        var activity = await CreateCompletedActivityAsync(RnaTaskName, "active", DefaultExpectedResults);
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);

        var form = await GetActivityQuestionsAsync(collectionId, activity.Id);
        var custom = form.Questions.Should().ContainSingle(q => q.Label == "What progress have you made towards 'Key result one'?").Subject;

        var response = await SaveAnswersAsync(collectionId, activity.Id, saveAsDraft: false, (custom.Code, null));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ToResponseModel<AnswersInvalidEnvelope>()).AnswerErrors
            .Should().Contain(e => e.QuestionCode == custom.Code && e.Message == "Enter an update for Key result one");
    }

    // --- Character limits on the free-text report pages ---

    [Fact]
    public async Task ShouldCapTheActivityDeliveryQuestions_At500Characters()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await CreateOpenCollectionAsync();
        var activity = await CreateRnaActivityAsync();
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);

        var form = await GetActivityQuestionsAsync(collectionId, activity.Id);
        form.Questions.Should().ContainSingle(q => q.Code == "AKR_DELIVERY_PROGRESS")
            .Which.MaxLength.Should().Be(500);

        var response = await SaveAnswersAsync(
            collectionId, activity.Id, saveAsDraft: true, ("AKR_DELIVERY_PROGRESS", new string('a', 501)));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ToResponseModel<AnswersInvalidEnvelope>()).AnswerErrors
            .Should().ContainSingle(e => e.QuestionCode == "AKR_DELIVERY_PROGRESS")
            .Which.Message.Should().Be("Description of progress must be 500 characters or less");
    }

    [Theory]
    [InlineData("SF_OTHER_PREVENTION", "Description of other gambling harms prevention work must be 500 characters or less")]
    [InlineData("SF_CHALLENGES", "Description of challenges must be 500 characters or less")]
    [InlineData("SF_DHSC_SUPPORT", "Feedback must be 500 characters or less")]
    public async Task ShouldCapTheStrategicFeedbackQuestions_At500Characters(string code, string expectedMessage)
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await StartConfirmedReportAsync();

        var feedback = await GetStrategicFeedbackAsync(collectionId);
        feedback.Pages.SelectMany(p => p.Questions).Should().ContainSingle(q => q.Code == code)
            .Which.MaxLength.Should().Be(500);

        var response = await SaveStrategicFeedbackAsync(collectionId, saveAsDraft: true, (code, new string('a', 501)));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ToResponseModel<AnswersInvalidEnvelope>()).AnswerErrors
            .Should().ContainSingle(e => e.QuestionCode == code)
            .Which.Message.Should().Be(expectedMessage);
    }

    // Sheet 2.4.4: the protocol marks this "Checkboxes", but it is a yes/no — kept as radios with its blue error
    // message taken as written (BLUM-7620).
    [Fact]
    public async Task ShouldAskForLearnings_AsYesNoWithTheProtocolsError()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await StartConfirmedReportAsync();

        var feedback = await GetStrategicFeedbackAsync(collectionId);
        var learnings = feedback.Pages.SelectMany(p => p.Questions).Should().ContainSingle(q => q.Code == "SF_LEARNINGS").Subject;
        learnings.QuestionType.Should().Be("Radio");
        learnings.Options.Select(o => o.Value).Should().Equal("yes", "no");

        var response = await SaveStrategicFeedbackAsync(collectionId, saveAsDraft: false, ("SF_LEARNINGS", null));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ToResponseModel<AnswersInvalidEnvelope>()).AnswerErrors
            .Should().Contain(e => e.QuestionCode == "SF_LEARNINGS" && e.Message == "Select yes if you have learnings to share");
    }

    // --- Funding spend: whole pounds, no minimum, and the activity/quarter named in every message ---

    [Fact]
    public async Task ShouldRejectPence_NamingTheActivity()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await CreateOpenCollectionAsync();
        var activity = await CreateRnaActivityAsync(title: "Regional needs assessment");
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);

        var response = await SaveFundingActualsAsync(collectionId, saveAsDraft: true, otherAmount: 0m, (activity.Id, 1500.50m));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.ToResponseModel<AnswersInvalidEnvelope>()).AnswerErrors
            .Should().ContainSingle(e => e.QuestionCode == $"actual-{activity.Id}")
            .Which.Message.Should().Be("Amount spent on Regional needs assessment must be a whole number and not include pence");

        (await Context.SubmissionSpendEntries.AsNoTracking().AnyAsync()).Should().BeFalse();
    }

    // The protocol struck the "amount of 1 or more" rule through — £0 was already allowed and stays allowed, and
    // there is no longer any lower bound above it other than "not negative".
    [Fact]
    public async Task ShouldAcceptAnAmountOfOne_NowThatTheMinimumIsGone()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await CreateOpenCollectionAsync();
        var activity = await CreateRnaActivityAsync();
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);

        var response = await SaveFundingActualsAsync(collectionId, saveAsDraft: true, otherAmount: 0m, (activity.Id, 1m));

        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
        (await Context.SubmissionSpendEntries.AsNoTracking().ToListAsync()).Should().HaveCount(2);
    }

    [Fact]
    public async Task ShouldNameTheActivity_WhenAnActualAmountIsMissingOnComplete()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await CreateOpenCollectionAsync();
        var activity = await CreateRnaActivityAsync(title: "Regional needs assessment");
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);

        var response = await SaveFundingActualsAsync(collectionId, saveAsDraft: false, otherAmount: null, (activity.Id, null));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var errors = (await response.ToResponseModel<AnswersInvalidEnvelope>()).AnswerErrors;
        errors.Should().ContainSingle(e => e.QuestionCode == $"actual-{activity.Id}")
            .Which.Message.Should().Be("Enter the amount spent on Regional needs assessment");
        errors.Should().ContainSingle(e => e.QuestionCode == "other-spend")
            .Which.Message.Should().Be("Enter the other spend this quarter");
    }

    [Fact]
    public async Task ShouldNameTheActivityAndQuarter_WhenAForecastAmountIsMissingOnComplete()
    {
        AuthenticateAsMainOrgLead();
        var collectionId = await CreateOpenCollectionAsync();
        var activity = await CreateRnaActivityAsync(title: "Regional needs assessment");
        await CreateSubmissionAsync(collectionId);
        await ConfirmActivitiesAsync(collectionId);

        var funding = await GetFundingSpendAsync(collectionId);
        var quarter = funding.RemainingQuarters.First().Quarter;

        var response = await SaveFundingForecastAsync(collectionId, saveAsDraft: false, [], []);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var errors = (await response.ToResponseModel<AnswersInvalidEnvelope>()).AnswerErrors;
        errors.Should().ContainSingle(e => e.QuestionCode == $"forecast-{activity.Id}-q{quarter}")
            .Which.Message.Should().Be($"Enter expected spend on Regional needs assessment for Quarter {quarter}");
        errors.Should().ContainSingle(e => e.QuestionCode == $"other-forecast-q{quarter}")
            .Which.Message.Should().Be($"Enter the amount of other planned spend for Quarter {quarter}");
    }
}