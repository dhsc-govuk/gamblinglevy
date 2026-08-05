using Application.Submissions.Protocol;
using Domain.Forms;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Unit.Submissions;

public class SubmissionAnswerValidatorTests
{
    private static ResolvedQuestion Question(
        string code,
        string type = QuestionTypes.Text,
        bool isRequired = true,
        string? conditionalQuestionCode = null,
        string? conditionalValue = null,
        IReadOnlyList<ResolvedQuestionOption>? options = null,
        decimal? min = null,
        decimal? max = null,
        bool isPercentage = false,
        string? errorMessage = null) =>
        new(
            Guid.NewGuid(),
            code,
            $"Label for {code}",
            null,
            type,
            isRequired,
            DisplayOrder: 0,
            conditionalQuestionCode,
            conditionalValue,
            options ?? [],
            min,
            max,
            isPercentage,
            PrefillSource: null,
            errorMessage,
            Section: KeyResultQuestionResolver.ResultsSection);

    private static Dictionary<string, string?> Answers(params (string Code, string? Value)[] answers) =>
        answers.ToDictionary(a => a.Code, a => a.Value, StringComparer.Ordinal);

    [Fact]
    public void IsVisible_ConditionalChild_HiddenUntilParentMatches()
    {
        var child = Question("CHILD", conditionalQuestionCode: "PARENT", conditionalValue: "other");

        SubmissionAnswerValidator.IsVisible(child, Answers()).Should().BeFalse();
        SubmissionAnswerValidator.IsVisible(child, Answers(("PARENT", "something"))).Should().BeFalse();
        SubmissionAnswerValidator.IsVisible(child, Answers(("PARENT", "other"))).Should().BeTrue();
    }

    [Fact]
    public void IsVisible_CommaListConditionalValue_MatchesAnyListedValue()
    {
        var child = Question("CHILD", conditionalQuestionCode: "PARENT", conditionalValue: "a,b");

        SubmissionAnswerValidator.IsVisible(child, Answers(("PARENT", "b"))).Should().BeTrue();
        SubmissionAnswerValidator.IsVisible(child, Answers(("PARENT", "c"))).Should().BeFalse();
    }

    [Fact]
    public void IsVisible_CheckboxParentAnswer_MatchesAnySelectedValue()
    {
        var child = Question("CHILD", conditionalQuestionCode: "PARENT", conditionalValue: "other");

        SubmissionAnswerValidator.IsVisible(child, Answers(("PARENT", "licensing,other"))).Should().BeTrue();
        SubmissionAnswerValidator.IsVisible(child, Answers(("PARENT", "licensing,planning"))).Should().BeFalse();
    }

    [Fact]
    public void Validate_EmptyRequired_OnlyFailsWhenEnforcingRequired()
    {
        var questions = new[] { Question("Q1", errorMessage: "Select who is contributing evidence") };

        SubmissionAnswerValidator.Validate(questions, Answers(), enforceRequired: false).Should().BeEmpty();

        var errors = SubmissionAnswerValidator.Validate(questions, Answers(), enforceRequired: true);
        errors.Should().ContainSingle();
        errors[0].QuestionCode.Should().Be("Q1");
        errors[0].Message.Should().Be("Select who is contributing evidence");
    }

    [Fact]
    public void Validate_HiddenRequiredQuestion_IsNotEnforced()
    {
        var questions = new[]
        {
            Question("CHILD", conditionalQuestionCode: "PARENT", conditionalValue: "other"),
        };

        SubmissionAnswerValidator.Validate(questions, Answers(), enforceRequired: true).Should().BeEmpty();
    }

    [Fact]
    public void Validate_NumberAnswer_RejectsNonWholeNumbers()
    {
        var questions = new[] { Question("Q_NUM", QuestionTypes.Number) };

        var errors = SubmissionAnswerValidator.Validate(questions, Answers(("Q_NUM", "12.5")), enforceRequired: false);

        errors.Should().ContainSingle();
        errors[0].Message.Should().Be("“Label for Q_NUM” must be a whole number.");
    }

    [Fact]
    public void Validate_NonNumericPercentage_UsesPercentagePhrasing()
    {
        var questions = new[] { Question("Q_PCT", QuestionTypes.Number, min: 0, max: 100, isPercentage: true) };

        var errors = SubmissionAnswerValidator.Validate(questions, Answers(("Q_PCT", "abc")), enforceRequired: false);

        errors.Should().ContainSingle();
        errors[0].Message.Should().Be("Enter a percentage, like 25");
    }

    [Fact]
    public void Validate_PercentageOutOfRange_UsesBoundedPercentageMessage()
    {
        var questions = new[] { Question("Q_PCT", QuestionTypes.Number, min: 0, max: 100, isPercentage: true) };

        var errors = SubmissionAnswerValidator.Validate(questions, Answers(("Q_PCT", "150")), enforceRequired: false);

        errors.Should().ContainSingle();
        errors[0].Message.Should().Be("Enter a percentage between 0 and 100");
    }

    [Fact]
    public void Validate_IntegerBelowMinimum_UsesMinimumMessage()
    {
        var questions = new[] { Question("Q_NUM", QuestionTypes.Number, min: 1) };

        var errors = SubmissionAnswerValidator.Validate(questions, Answers(("Q_NUM", "0")), enforceRequired: false);

        errors.Should().ContainSingle();
        errors[0].Message.Should().Be("Enter a number of 1 or more");
    }

    [Fact]
    public void Validate_RadioAnswer_MustBeAKnownOption()
    {
        var options = new List<ResolvedQuestionOption> { new("yes", "Yes"), new("no", "No") };
        var questions = new[] { Question("Q_RADIO", QuestionTypes.Radio, options: options) };

        SubmissionAnswerValidator.Validate(questions, Answers(("Q_RADIO", "yes")), enforceRequired: false).Should().BeEmpty();

        var errors = SubmissionAnswerValidator.Validate(questions, Answers(("Q_RADIO", "maybe")), enforceRequired: false);
        errors.Should().ContainSingle();
        errors[0].QuestionCode.Should().Be("Q_RADIO");
    }

    [Fact]
    public void Validate_CheckboxAnswer_EverySelectedValueMustBeAKnownOption()
    {
        var options = new List<ResolvedQuestionOption> { new("a", "A"), new("b", "B") };
        var questions = new[] { Question("Q_CHECK", QuestionTypes.Checkbox, options: options) };

        SubmissionAnswerValidator.Validate(questions, Answers(("Q_CHECK", "a,b")), enforceRequired: false).Should().BeEmpty();

        var errors = SubmissionAnswerValidator.Validate(questions, Answers(("Q_CHECK", "a,c")), enforceRequired: false);
        errors.Should().ContainSingle();
        errors[0].QuestionCode.Should().Be("Q_CHECK");
    }

    [Fact]
    public void CollectPersistable_DropsHiddenAndBlankAnswers()
    {
        var questions = new[]
        {
            Question("PARENT"),
            Question("CHILD", conditionalQuestionCode: "PARENT", conditionalValue: "other"),
            Question("BLANK", isRequired: false),
        };
        var answers = Answers(
            ("PARENT", "  something  "),
            ("CHILD", "should be dropped — parent is not 'other'"),
            ("BLANK", "   "));

        var persistable = SubmissionAnswerValidator.CollectPersistable(questions, answers);

        persistable.Should().ContainSingle();
        persistable[0].Question.Code.Should().Be("PARENT");
        persistable[0].Value.Should().Be("something");
    }
}