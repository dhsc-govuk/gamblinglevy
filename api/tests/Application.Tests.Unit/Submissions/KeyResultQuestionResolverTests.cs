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
            title: "Test activity");

    private static ActivityExpectedResult StandardResult(Activity activity, string code, int order = 0) =>
        ActivityExpectedResult.Record(
            ActivityExpectedResultId.New(), activity.Id, $"Label for {code}", order, Actor, code);

    private static ActivityExpectedResult CustomResult(Activity activity, string text, int order = 0) =>
        ActivityExpectedResult.Record(ActivityExpectedResultId.New(), activity.Id, text, order, Actor);

    private static KeyResultQuestion BankQuestion(
        string keyResultCode,
        string code,
        QuestionStatusScope scope,
        string label,
        ProtocolFieldType fieldType,
        int displayOrder = 0,
        IReadOnlyList<KeyResultQuestionOption>? options = null,
        string? prefillSource = null) =>
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
            options: options,
            prefillSource: prefillSource);

    private static IReadOnlyList<DcFormQuestion> DeliveryQuestions(params (string Code, bool IsActive)[] specs)
    {
        var module = FormModule.Create(FormModuleId.New(), "activity_key_results", "Enter activity key results", 1, Actor);
        var questions = new List<DcFormQuestion>();
        var order = 0;

        foreach (var (code, isActive) in specs)
        {
            var question = module.AddQuestion(
                DcFormQuestionId.New(), code, $"Label {code}", QuestionType.Textarea, order++, Actor, isRequired: true);
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
        ActivityAnswer.Record(ActivityAnswerId.New(), ActivityId.New(), FormQuestionId.New(), questionCode, value, Actor);

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
        resolved[0].Label.Should().Be("Provide progress details for: Reach 100 residents");
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
}