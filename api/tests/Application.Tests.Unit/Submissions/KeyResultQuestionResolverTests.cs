using Application.Common.Interfaces.Queries;
using Application.Submissions.Protocol;
using Domain.Activities;
using Domain.Categories;
using Domain.DataCollectionForms;
using Domain.Forms;
using Domain.KeyResults;
using Domain.Organisations;
using Domain.Users;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Unit.Submissions;

public class KeyResultQuestionResolverTests
{
    private static readonly UserId Actor = UserId.New();
    private static readonly ActivityTaskId TaskId = ActivityTaskId.New();

    private static Activity BuildActivity(ActivityStatus status) =>
        Activity.NewComplete(
            ActivityId.New(),
            OrganisationId.New(),
            Actor,
            CategoryId.New(),
            TaskId,
            status,
            title: "Test activity",
            utcNow: DateTime.UtcNow);

    private static ActivityExpectedResult StandardResult(Activity activity, string code, int order = 0) =>
        ActivityExpectedResult.Record(
            ActivityExpectedResultId.New(), activity.Id, $"Label for {code}", order, Actor, DateTime.UtcNow, code);

    private static ActivityExpectedResult CustomResult(Activity activity, string text, int order = 0) =>
        ActivityExpectedResult.Record(ActivityExpectedResultId.New(), activity.Id, text, order, Actor, DateTime.UtcNow);

    private static KeyResultQuestion BankQuestion(
        string keyResultCode,
        string code,
        QuestionStatusScope scope,
        string label,
        ProtocolFieldType fieldType,
        int displayOrder = 0,
        IReadOnlyList<KeyResultQuestionOption>? options = null,
        string? prefillSource = null,
        decimal? minValue = null,
        decimal? maxValue = null) =>
        KeyResultQuestion.Seed(
            KeyResultQuestionId.New(),
            TaskId,
            keyResultCode,
            code,
            scope,
            label,
            fieldType,
            displayOrder,
            Actor,
            minValue: minValue,
            maxValue: maxValue,
            options: options,
            prefillSource: prefillSource,
            utcNow: DateTime.UtcNow);

    private static IReadOnlyList<DcFormQuestion> DeliveryQuestions(params (string Code, bool IsActive)[] specs)
    {
        var module = FormModule.Create(FormModuleId.New(), "activity_key_results", "Enter activity results", 1, Actor, DateTime.UtcNow);
        var questions = new List<DcFormQuestion>();
        var order = 0;

        foreach (var (code, isActive) in specs)
        {
            var question = module.AddQuestion(
                DcFormQuestionId.New(), code, $"Label {code}", QuestionType.Textarea, order++, Actor, isRequired: true, utcNow: DateTime.UtcNow);
            if (!isActive)
            {
                question.Deactivate();
            }

            questions.Add(question);
        }

        return questions;
    }

    private static ResolvedQuestion Resolved(string code, string? prefillSource = null) =>
        new(
            Guid.NewGuid(),
            code,
            $"Label for {code}",
            null,
            QuestionTypes.Text,
            IsRequired: true,
            DisplayOrder: 0,
            ConditionalQuestionCode: null,
            ConditionalValue: null,
            Options: [],
            MinValue: null,
            MaxValue: null,
            IsPercentage: false,
            PrefillSource: prefillSource,
            ErrorMessage: null,
            Section: KeyResultQuestionResolver.ResultsSection);

    private static ActivityAnswer Answer(string questionCode, string value) =>
        ActivityAnswer.Record(ActivityAnswerId.New(), ActivityId.New(), FormQuestionId.New(), questionCode, value, Actor, DateTime.UtcNow);

    // Two earlier reports for the carry-forward cases. Rows arrive most-recent-period-first, so a row tagged
    // OlderSubmission is one the query ordered behind NewerSubmission's.
    private static readonly Guid NewerSubmission = Guid.NewGuid();
    private static readonly Guid OlderSubmission = Guid.NewGuid();

    private static PriorActivityAnswerRow Prior(Guid submissionId, string questionCode, string value) =>
        new(submissionId, questionCode, value);

    // BLUM-7790 removed LSG_ATTENDANCE, which was the protocol's only Percentage question and therefore the only
    // thing exercising this mapping end to end (the seeded coverage went with it, and
    // KeyResultProtocolSeedDataTests.PercentageQuestions_AreBoundedZeroToOneHundred now iterates an empty set).
    // The plumbing stays because the protocol has carried percentage questions before and may again, so pin it
    // here instead: a Percentage field type must reach the client as a Number carrying its bounds, and must set
    // IsPercentage, which is what QuarterlyReportControllerService turns into the "%" suffix and what makes
    // SubmissionAnswerValidator say "Enter a percentage between 0 and 100" rather than the whole-number message.
    [Fact]
    public void Resolve_PercentageQuestion_MapsToANumberCarryingItsBoundsAndPercentageFlag()
    {
        var activity = BuildActivity(ActivityStatus.Active);
        var expectedResults = new[] { StandardResult(activity, "kr_a") };
        var bank = new[]
        {
            BankQuestion(
                "kr_a", "KR_PCT", QuestionStatusScope.Active, "What percentage?", ProtocolFieldType.Percentage,
                minValue: 0, maxValue: 100),
        };

        var resolved = KeyResultQuestionResolver.Resolve(activity, expectedResults, bank, []);

        var question = resolved.Should().ContainSingle().Subject;
        question.QuestionType.Should().Be(QuestionTypes.Number);
        question.IsPercentage.Should().BeTrue();
        question.MinValue.Should().Be(0);
        question.MaxValue.Should().Be(100);
    }

    // The negative half: a non-percentage numeric question must NOT claim the percentage phrasing, or every
    // integer question inherits "Enter a percentage, like 25".
    [Fact]
    public void Resolve_IntegerQuestion_IsANumberButNotAPercentage()
    {
        var activity = BuildActivity(ActivityStatus.Active);
        var expectedResults = new[] { StandardResult(activity, "kr_a") };
        var bank = new[]
        {
            BankQuestion("kr_a", "KR_INT", QuestionStatusScope.Active, "How many?", ProtocolFieldType.Integer),
        };

        var resolved = KeyResultQuestionResolver.Resolve(activity, expectedResults, bank, []);

        var question = resolved.Should().ContainSingle().Subject;
        question.QuestionType.Should().Be(QuestionTypes.Number);
        question.IsPercentage.Should().BeFalse();
    }

    [Fact]
    public void Resolve_ActiveActivity_PicksActivePhrasingVariant()
    {
        var activity = BuildActivity(ActivityStatus.Active);
        var expectedResults = new[] { StandardResult(activity, "kr_a") };
        var bank = new[]
        {
            BankQuestion("kr_a", "KR_Q", QuestionStatusScope.Active, "Active phrasing", ProtocolFieldType.Text, 0),
            BankQuestion("kr_a", "KR_Q", QuestionStatusScope.ClosedFinished, "Closed phrasing", ProtocolFieldType.Text, 1),
        };

        var resolved = KeyResultQuestionResolver.Resolve(activity, expectedResults, bank, []);

        resolved.Should().ContainSingle();
        resolved[0].Code.Should().Be("KR_Q");
        resolved[0].Label.Should().Be("Active phrasing");
        resolved[0].Section.Should().Be(KeyResultQuestionResolver.ResultsSection);
    }

    [Fact]
    public void Resolve_ClosedActivity_PicksClosedFinishedPhrasingVariant()
    {
        var activity = BuildActivity(ActivityStatus.Closed);
        var expectedResults = new[] { StandardResult(activity, "kr_a") };
        var bank = new[]
        {
            BankQuestion("kr_a", "KR_Q", QuestionStatusScope.Active, "Active phrasing", ProtocolFieldType.Text, 0),
            BankQuestion("kr_a", "KR_Q", QuestionStatusScope.ClosedFinished, "Closed phrasing", ProtocolFieldType.Text, 1),
        };

        var resolved = KeyResultQuestionResolver.Resolve(activity, expectedResults, bank, []);

        resolved.Should().ContainSingle();
        resolved[0].Label.Should().Be("Closed phrasing");
    }

    [Fact]
    public void Resolve_FinishedScopedQuestion_OnlyReturnedForFinishedActivities()
    {
        var bank = new[]
        {
            BankQuestion("kr_a", "KR_Q", QuestionStatusScope.ClosedFinished, "Closed phrasing", ProtocolFieldType.Text, 0),
            BankQuestion("kr_a", "KR_Q_CHILD", QuestionStatusScope.Finished, "Finished-only follow-up", ProtocolFieldType.Text, 1),
        };

        var closed = BuildActivity(ActivityStatus.Closed);
        var closedResolved = KeyResultQuestionResolver.Resolve(closed, [StandardResult(closed, "kr_a")], bank, []);

        var finished = BuildActivity(ActivityStatus.Finished);
        var finishedResolved = KeyResultQuestionResolver.Resolve(finished, [StandardResult(finished, "kr_a")], bank, []);

        closedResolved.Select(q => q.Code).Should().Equal("KR_Q");
        finishedResolved.Select(q => q.Code).Should().Equal("KR_Q", "KR_Q_CHILD");
    }

    [Fact]
    public void Resolve_ReturnsOnlySelectedKeyResultsQuestions()
    {
        var activity = BuildActivity(ActivityStatus.Active);
        var expectedResults = new[] { StandardResult(activity, "kr_a") };
        var bank = new[]
        {
            BankQuestion("kr_a", "KR_A_Q", QuestionStatusScope.Active, "Selected", ProtocolFieldType.Text, 0),
            BankQuestion("kr_b", "KR_B_Q", QuestionStatusScope.Active, "Not selected", ProtocolFieldType.Text, 1),
        };

        var resolved = KeyResultQuestionResolver.Resolve(activity, expectedResults, bank, []);

        resolved.Select(q => q.Code).Should().Equal("KR_A_Q");
    }

    [Fact]
    public void Resolve_PreservesKeyResultSelectionOrder()
    {
        var activity = BuildActivity(ActivityStatus.Active);
        var expectedResults = new[]
        {
            StandardResult(activity, "kr_b", order: 0),
            StandardResult(activity, "kr_a", order: 1),
        };
        var bank = new[]
        {
            BankQuestion("kr_a", "KR_A_Q", QuestionStatusScope.Active, "A", ProtocolFieldType.Text, 0),
            BankQuestion("kr_b", "KR_B_Q", QuestionStatusScope.Active, "B", ProtocolFieldType.Text, 1),
        };

        var resolved = KeyResultQuestionResolver.Resolve(activity, expectedResults, bank, []);

        resolved.Select(q => q.Code).Should().Equal("KR_B_Q", "KR_A_Q");
        resolved.Select(q => q.DisplayOrder).Should().Equal(0, 1);
    }

    [Fact]
    public void Resolve_CustomExpectedResult_SynthesisesRequiredTextarea()
    {
        var activity = BuildActivity(ActivityStatus.Active);
        var custom = CustomResult(activity, "Reach 100 residents");

        var resolved = KeyResultQuestionResolver.Resolve(activity, [custom], [], []);

        resolved.Should().ContainSingle();
        resolved[0].Code.Should().Be($"CUSTOM_{custom.Id.Value:N}");
        resolved[0].Label.Should().Be("What progress have you made towards 'Reach 100 residents'?");
        resolved[0].QuestionType.Should().Be(QuestionTypes.Textarea);
        resolved[0].IsRequired.Should().BeTrue();
        resolved[0].Section.Should().Be(KeyResultQuestionResolver.ResultsSection);
    }

    [Fact]
    public void Resolve_AppendsActiveDeliveryQuestionsWithDeliverySection()
    {
        var activity = BuildActivity(ActivityStatus.Active);
        var expectedResults = new[] { StandardResult(activity, "kr_a") };
        var bank = new[]
        {
            BankQuestion("kr_a", "KR_A_Q", QuestionStatusScope.Active, "A", ProtocolFieldType.Text, 0),
        };
        var delivery = DeliveryQuestions(("AKR_PROGRESS", true), ("AKR_RETIRED", false));

        var resolved = KeyResultQuestionResolver.Resolve(activity, expectedResults, bank, delivery);

        resolved.Select(q => q.Code).Should().Equal("KR_A_Q", "AKR_PROGRESS");
        resolved.Should().NotContain(q => q.Code == "AKR_RETIRED");
        resolved.Last().Section.Should().Be(KeyResultQuestionResolver.DeliverySection);
    }

    // BLUM-9104: a restore brings back, withdrawn, an option a later version published. Options is what the form, the
    // offline file and the validator read, so it must not be there; it travels in WithdrawnOptions instead, so an
    // answer given with it still finds its label.
    [Fact]
    public void Resolve_ADeliveryQuestionsWithdrawnOption_IsNeitherOfferedNorAccepted()
    {
        var activity = BuildActivity(ActivityStatus.Active);
        var module = FormModule.Create(FormModuleId.New(), "activity_key_results", "Enter activity results", 1, Actor, DateTime.UtcNow);
        var question = module.AddQuestion(DcFormQuestionId.New(), "AKR_ON_TRACK", "On track?", QuestionType.Radio, 0, Actor, DateTime.UtcNow);
        question.AddOption(DcFormQuestionOptionId.New(), "yes", "Yes", 1);
        question.AddOption(DcFormQuestionOptionId.New(), "partly", "Partly", 2, isActive: false);
        question.AddOption(DcFormQuestionOptionId.New(), "no", "No", 3);

        var resolved = KeyResultQuestionResolver.Resolve(activity, [], [], [question]).Single();

        resolved.Options.Select(o => o.Value).Should().Equal("yes", "no");
        resolved.WithdrawnOptions.Should().Equal(new ResolvedQuestionOption("partly", "Partly"));
        SubmissionAnswerValidator.Validate([resolved], new Dictionary<string, string?> { ["AKR_ON_TRACK"] = "partly" }, enforceRequired: false, transitive: false)
            .Should().ContainSingle(error => error.QuestionCode == "AKR_ON_TRACK");
        SubmissionAnswerValidator.Validate([resolved], new Dictionary<string, string?> { ["AKR_ON_TRACK"] = "yes" }, enforceRequired: false, transitive: false)
            .Should().BeEmpty();
    }

    // BLUM-8090: a Joint partnership is asked the module's delivery questions in its own wording. Only the copy
    // changes — code, section, required flag and length limit are the module's — and only on the delivery page: a
    // results or custom question with the same code is left alone, as is a delivery question the copy does not name.
    [Fact]
    public void Resolve_ForAJointPartnership_RewordsOnlyTheDeliveryQuestions()
    {
        var activity = BuildActivity(ActivityStatus.Active);
        var custom = CustomResult(activity, "Reach 100 residents");
        var delivery = DeliveryQuestions(("AKR_DELIVERY_PROGRESS", true), ("AKR_DELIVERY_CHALLENGES", true), ("AKR_OTHER", true));

        var generic = KeyResultQuestionResolver.Resolve(activity, [custom], [], delivery);
        var partnership = KeyResultQuestionResolver.Resolve(activity, [custom], [], delivery, isJointPartnership: true);

        partnership.Select(q => (q.Code, q.Section, q.IsRequired, q.DisplayOrder))
            .Should().Equal(generic.Select(q => (q.Code, q.Section, q.IsRequired, q.DisplayOrder)));

        var progress = partnership.Single(q => q.Code == "AKR_DELIVERY_PROGRESS");
        progress.Label.Should().Be("What progress have you made this quarter?");
        progress.Hint.Should().Be("Tell us about where you're at in the partnership process");
        progress.ErrorMessage.Should().Be("Enter a description of the progress you have made this quarter");
        partnership.Single(q => q.Code == "AKR_DELIVERY_CHALLENGES").Hint
            .Should().Be("Use this space to add any information about your challenges so far");

        partnership.Single(q => q.Code == "AKR_OTHER").Should().BeEquivalentTo(generic.Single(q => q.Code == "AKR_OTHER"));
        partnership.Single(q => q.Section == KeyResultQuestionResolver.ResultsSection)
            .Should().BeEquivalentTo(generic.Single(q => q.Section == KeyResultQuestionResolver.ResultsSection));
    }

    [Fact]
    public void ResolveAnswered_ForAJointPartnership_LabelsDeliveryAnswersInThePartnershipWording()
    {
        var activity = BuildActivity(ActivityStatus.Active);
        var delivery = DeliveryQuestions(("AKR_DELIVERY_PROGRESS", true));
        var answers = new Dictionary<string, string> { ["AKR_DELIVERY_PROGRESS"] = "Agreed the pooled budget" };

        var answered = KeyResultQuestionResolver.ResolveAnswered(activity, [], [], delivery, answers, isJointPartnership: true);

        answered.Should().ContainSingle().Which.Question.Label.Should().Be("What progress have you made this quarter?");
    }

    [Fact]
    public void Resolve_BooleanQuestion_BecomesRadioWithYesNoOptions()
    {
        var activity = BuildActivity(ActivityStatus.Active);
        var expectedResults = new[] { StandardResult(activity, "kr_a") };
        var bank = new[]
        {
            BankQuestion("kr_a", "KR_BOOL", QuestionStatusScope.Active, "Yes or no?", ProtocolFieldType.Boolean, 0),
        };

        var resolved = KeyResultQuestionResolver.Resolve(activity, expectedResults, bank, []);

        resolved.Should().ContainSingle();
        resolved[0].QuestionType.Should().Be(QuestionTypes.Radio);
        resolved[0].Options.Select(o => o.Value).Should().Equal("yes", "no");
    }

    [Fact]
    public void BuildPrefill_MapsWhoWithSuffixedActivityAnswers()
    {
        var questions = new[]
        {
            Resolved("KR_CONTRIBUTORS", KeyResultQuestionResolver.WhoWithPrefillSource),
            Resolved("KR_CONTRIBUTORS_OTHER", KeyResultQuestionResolver.WhoWithOtherPrefillSource),
            Resolved("KR_PLAIN"),
        };
        var answers = new[]
        {
            Answer("NA_WHO_WITH", "licensing,other"),
            Answer("NA_WHO_WITH_OTHER", "Partner org"),
        };

        var prefill = KeyResultQuestionResolver.BuildPrefill(questions, answers);

        prefill.Should().HaveCount(2);
        prefill["KR_CONTRIBUTORS"].Should().Be("licensing,other");
        prefill["KR_CONTRIBUTORS_OTHER"].Should().Be("Partner org");
    }

    [Fact]
    public void BuildPrefill_WhoWithDoesNotMatchTheWhoWithOtherAnswer()
    {
        var questions = new[] { Resolved("KR_CONTRIBUTORS", KeyResultQuestionResolver.WhoWithPrefillSource) };
        var answers = new[] { Answer("NA_WHO_WITH_OTHER", "Partner org") };

        var prefill = KeyResultQuestionResolver.BuildPrefill(questions, answers);

        prefill.Should().BeEmpty();
    }

    // --- BLUM-7671 carry-forward ---

    [Theory]
    [InlineData(ActivityStatuses.Planned, false)]
    [InlineData(ActivityStatuses.Active, false)]
    [InlineData(ActivityStatuses.Closed, false)]
    [InlineData(ActivityStatuses.Finished, true)]
    public void CarriesForwardPreviousAnswers_OnlyForAFinishedActivity(string status, bool expected)
    {
        KeyResultQuestionResolver.CarriesForwardPreviousAnswers(ActivityStatus.From(status)).Should().Be(expected);
    }

    [Fact]
    public void CarriesForwardPreviousAnswers_IsFalseForARecordWithNoStatus()
    {
        KeyResultQuestionResolver.CarriesForwardPreviousAnswers(null).Should().BeFalse();
    }

    [Fact]
    public void BuildCarryForward_TakesTheFirstValueSeenPerCode()
    {
        var questions = new[] { Resolved("KR_A"), Resolved("KR_B") };

        // Rows arrive most-recent-period-first, so the newer report's value must win.
        var prior = new[]
        {
            Prior(NewerSubmission, "KR_A", "newer"),
            Prior(NewerSubmission, "KR_A", "older"),
            Prior(NewerSubmission, "KR_B", "only"),
        };

        var carried = KeyResultQuestionResolver.BuildCarryForward(questions, prior);

        carried.Should().HaveCount(2);
        carried["KR_A"].Should().Be("newer");
        carried["KR_B"].Should().Be("only");
    }

    // The carry-forward is one report's answer set taken whole, never a per-code merge across several. A blank
    // is never persisted, so a code the LA deliberately cleared in the newer report is indistinguishable from
    // one it never answered — filling the gap from an older report would resurrect data the LA had deleted.
    [Fact]
    public void BuildCarryForward_IgnoresRowsFromAnOlderSubmission()
    {
        var questions = new[] { Resolved("KR_A"), Resolved("KR_B") };
        var prior = new[]
        {
            Prior(NewerSubmission, "KR_A", "newer"),
            Prior(OlderSubmission, "KR_A", "older"),
            Prior(OlderSubmission, "KR_B", "cleared in the newer report"),
        };

        var carried = KeyResultQuestionResolver.BuildCarryForward(questions, prior);

        carried.Should().ContainSingle();
        carried["KR_A"].Should().Be("newer");
    }

    // An activity that was Active in an earlier quarter saved answers under codes the closed/finished phrasing
    // never asks. Those must not come back as values for questions that do not exist on this form.
    [Fact]
    public void BuildCarryForward_DropsCodesThatNoLongerResolveToAQuestion()
    {
        var questions = new[] { Resolved("KR_A") };
        var prior = new[]
        {
            Prior(NewerSubmission, "KR_A", "kept"),
            Prior(NewerSubmission, "KR_ACTIVE_ONLY", "dropped"),
        };

        var carried = KeyResultQuestionResolver.BuildCarryForward(questions, prior);

        carried.Should().ContainSingle();
        carried["KR_A"].Should().Be("kept");
    }

    // A blank must not be carried: nothing else in the report stores one, and a carried blank would be written
    // as a real answer row saying the LA answered an optional question with nothing.
    [Fact]
    public void BuildCarryForward_SkipsBlankValues()
    {
        var questions = new[] { Resolved("KR_A"), Resolved("KR_B") };
        var prior = new[]
        {
            Prior(NewerSubmission, "KR_A", string.Empty),
            Prior(NewerSubmission, "KR_B", "   "),
        };

        var carried = KeyResultQuestionResolver.BuildCarryForward(questions, prior);

        carried.Should().BeEmpty();
    }

    [Fact]
    public void BuildCarryForward_WithNoPriorAnswers_IsEmpty()
    {
        KeyResultQuestionResolver.BuildCarryForward([Resolved("KR_A")], []).Should().BeEmpty();
    }

    // --- ResolveAnswered: labelling a finalised report's answers for the activity-record export (BLUM-7648) ---

    [Fact]
    public void ResolveAnswered_ListsOnlyAnsweredQuestions_InTheCurrentOrderAndWording()
    {
        var activity = BuildActivity(ActivityStatus.Active);
        var expectedResults = new[] { StandardResult(activity, "kr_a") };
        var bank = new[]
        {
            BankQuestion("kr_a", "KR_FIRST", QuestionStatusScope.Active, "First question", ProtocolFieldType.Text, 0),
            BankQuestion("kr_a", "KR_SECOND", QuestionStatusScope.Active, "Second question", ProtocolFieldType.Text, 1),
            BankQuestion("kr_a", "KR_UNANSWERED", QuestionStatusScope.Active, "Unanswered question", ProtocolFieldType.Text, 2),
        };
        var delivery = DeliveryQuestions(("AKR_DELIVERY_PROGRESS", true));
        var answers = new Dictionary<string, string>
        {
            ["AKR_DELIVERY_PROGRESS"] = "On track",
            ["KR_SECOND"] = "Second answer",
            ["KR_FIRST"] = "First answer",
        };

        var answered = KeyResultQuestionResolver.ResolveAnswered(activity, expectedResults, bank, delivery, answers);

        answered.Select(a => (a.Question.Label, a.Value)).Should().Equal(
            ("First question", "First answer"),
            ("Second question", "Second answer"),
            ("Label AKR_DELIVERY_PROGRESS", "On track"));
        answered[2].Question.Section.Should().Be(KeyResultQuestionResolver.DeliverySection);
    }

    [Fact]
    public void ResolveAnswered_ACodeTheCurrentStatusNoLongerAsks_KeepsItsProtocolLabelAndOptions()
    {
        // Reported while Active; the activity has since closed, so today's resolution asks the closed phrasing only.
        var activity = BuildActivity(ActivityStatus.Closed);
        var expectedResults = new[] { StandardResult(activity, "kr_a") };
        var bank = new[]
        {
            BankQuestion("kr_a", "KR_ACTIVE_ONLY", QuestionStatusScope.Active, "Will you publish it?", ProtocolFieldType.Boolean, 0),
            BankQuestion("kr_a", "KR_SHARED", QuestionStatusScope.ClosedFinished, "What did you achieve?", ProtocolFieldType.Text, 1),
        };
        var answers = new Dictionary<string, string> { ["KR_ACTIVE_ONLY"] = "yes" };

        var answered = KeyResultQuestionResolver.ResolveAnswered(activity, expectedResults, bank, [], answers);

        answered.Should().ContainSingle();
        answered[0].Question.Label.Should().Be("Will you publish it?");
        answered[0].Question.Options.Should().Contain(o => o.Value == "yes" && o.Label == "Yes");
        answered[0].Question.Section.Should().Be(KeyResultQuestionResolver.ResultsSection);
    }

    [Fact]
    public void ResolveAnswered_ARetiredDeliveryQuestion_IsStillLabelled()
    {
        var activity = BuildActivity(ActivityStatus.Active);
        var delivery = DeliveryQuestions(("AKR_RETIRED", false));
        var answers = new Dictionary<string, string> { ["AKR_RETIRED"] = "Old answer" };

        var answered = KeyResultQuestionResolver.ResolveAnswered(activity, [], [], delivery, answers);

        answered.Should().ContainSingle();
        answered[0].Question.Label.Should().Be("Label AKR_RETIRED");
        answered[0].Question.Section.Should().Be(KeyResultQuestionResolver.DeliverySection);
    }

    [Fact]
    public void ResolveAnswered_AnAnswerToARemovedCustomMeasure_IsLabelledByItsCode()
    {
        var activity = BuildActivity(ActivityStatus.Active);
        var answers = new Dictionary<string, string> { ["CUSTOM_0123456789abcdef0123456789abcdef"] = "Progress on a dropped measure" };

        var answered = KeyResultQuestionResolver.ResolveAnswered(activity, [], [], [], answers);

        answered.Should().ContainSingle();
        answered[0].Question.Label.Should().Be("CUSTOM_0123456789abcdef0123456789abcdef");
        answered[0].Value.Should().Be("Progress on a dropped measure");
    }

    [Fact]
    public void ResolveAnswered_SkipsBlankValues()
    {
        var activity = BuildActivity(ActivityStatus.Active);
        var answers = new Dictionary<string, string> { ["AKR_DELIVERY_PROGRESS"] = "  " };

        KeyResultQuestionResolver.ResolveAnswered(activity, [], [], DeliveryQuestions(("AKR_DELIVERY_PROGRESS", true)), answers)
            .Should().BeEmpty();
    }
}