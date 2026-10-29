using Application.Forms.Answers;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Unit.Forms;

// AnswerVisibility is the one show/hide check every validator, presenter, snapshot and offline file goes through
// (BLUM-9087 spike item 7). transitive: false is the one-level rule every copy applied before; transitive: true is
// BLUM_9087's, where a question or page whose parent is hidden is hidden too. Cases that read the same either way run
// in both modes.
public class AnswerVisibilityTests
{
    private static VisibilityNode Node(string code, string? parent = null, string? values = null, VisibilityRule? page = null) =>
        VisibilityNode.For(code, parent, values, page);

    private static Dictionary<string, string?> Answers(params (string Code, string? Value)[] answers) =>
        answers.ToDictionary(a => a.Code, a => a.Value, StringComparer.Ordinal);

    private static AnswerVisibility Visibility(bool transitive, Dictionary<string, string?> answers, params VisibilityNode[] questions) =>
        AnswerVisibility.Of(questions, answers, transitive);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IsVisible_WithNoRule_IsVisible_AsIsACodeOutsideTheSet(bool transitive)
    {
        var visibility = Visibility(transitive, Answers(), Node("PLAIN"));

        visibility.IsVisible("PLAIN").Should().BeTrue();
        visibility.IsVisible("UNKNOWN").Should().BeTrue();
        visibility.IsPageVisible(null).Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IsVisible_OneValue_IsHiddenUntilTheParentHoldsIt(bool transitive)
    {
        var questions = new[] { Node("PARENT"), Node("CHILD", "PARENT", "yes") };

        Visibility(transitive, Answers(), questions).IsVisible("CHILD").Should().BeFalse();
        Visibility(transitive, Answers(("PARENT", "no")), questions).IsVisible("CHILD").Should().BeFalse();
        Visibility(transitive, Answers(("PARENT", "yes")), questions).IsVisible("CHILD").Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IsVisible_SeveralValues_MatchesAnyOfThem_Trimmed(bool transitive)
    {
        var questions = new[] { Node("PARENT"), Node("CHILD", "PARENT", "yes , in_progress,other") };

        Visibility(transitive, Answers(("PARENT", "in_progress")), questions).IsVisible("CHILD").Should().BeTrue();
        Visibility(transitive, Answers(("PARENT", " other ")), questions).IsVisible("CHILD").Should().BeTrue();
        Visibility(transitive, Answers(("PARENT", "no")), questions).IsVisible("CHILD").Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IsVisible_CheckboxParent_MatchesAnyOfItsTicks(bool transitive)
    {
        var questions = new[] { Node("PARENT"), Node("CHILD", "PARENT", "other") };

        Visibility(transitive, Answers(("PARENT", "licensing, other")), questions).IsVisible("CHILD").Should().BeTrue();
        Visibility(transitive, Answers(("PARENT", "licensing,planning")), questions).IsVisible("CHILD").Should().BeFalse();
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(false, "")]
    [InlineData(false, "   ")]
    [InlineData(true, null)]
    [InlineData(true, "")]
    [InlineData(true, "   ")]
    public void IsVisible_BlankParent_HidesTheQuestion(bool transitive, string? parentAnswer)
    {
        var visibility = Visibility(transitive, Answers(("PARENT", parentAnswer)), Node("PARENT"), Node("CHILD", "PARENT", "yes"));

        visibility.IsVisible("CHILD").Should().BeFalse();
    }

    [Theory]
    [InlineData(null, "yes")]
    [InlineData("  ", "yes")]
    [InlineData("PARENT", null)]
    [InlineData("PARENT", "  ")]
    public void From_EitherHalfBlank_IsNoRule_SoTheQuestionIsVisible(string? parentCode, string? values)
    {
        VisibilityRule.From(parentCode, values).Should().BeNull();
        Visibility(transitive: true, Answers(), Node("PARENT"), Node("CHILD", parentCode, values)).IsVisible("CHILD").Should().BeTrue();
    }

    // A → B → C, with B answered "yes" but hidden because A says "no" (an answer left behind, or a hidden prefill).
    [Fact]
    public void IsVisible_ThreeLevelChain_OneLevelReadsTheHiddenParentsAnswer_TransitiveHidesTheGrandchild()
    {
        var questions = new[] { Node("A"), Node("B", "A", "yes"), Node("C", "B", "yes") };
        var answers = Answers(("A", "no"), ("B", "yes"), ("C", "something"));

        var oneLevel = Visibility(transitive: false, answers, questions);
        oneLevel.IsVisible("B").Should().BeFalse();
        oneLevel.IsVisible("C").Should().BeTrue();

        var transitive = Visibility(transitive: true, answers, questions);
        transitive.IsVisible("B").Should().BeFalse();
        transitive.IsVisible("C").Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IsVisible_ThreeLevelChain_EveryRuleHolding_ShowsTheGrandchild(bool transitive)
    {
        var visibility = Visibility(
            transitive,
            Answers(("A", "yes"), ("B", "yes")),
            Node("A"),
            Node("B", "A", "yes"),
            Node("C", "B", "yes"));

        visibility.IsVisible("C").Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IsVisible_OnAPage_NeedsThePagesRuleAndItsOwn(bool transitive)
    {
        var page = new VisibilityRule("GATE", ["yes"]);
        var questions = new[] { Node("GATE"), Node("Q1", page: page), Node("Q2", "Q1", "other", page) };

        var closed = Visibility(transitive, Answers(("GATE", "no"), ("Q1", "other")), questions);
        closed.IsPageVisible(page).Should().BeFalse();
        closed.IsVisible("Q1").Should().BeFalse();
        closed.IsVisible("Q2").Should().BeFalse();

        var open = Visibility(transitive, Answers(("GATE", "yes"), ("Q1", "other")), questions);
        open.IsPageVisible(page).Should().BeTrue();
        open.IsVisible("Q1").Should().BeTrue();
        open.IsVisible("Q2").Should().BeTrue();

        Visibility(transitive, Answers(("GATE", "yes"), ("Q1", "no")), questions).IsVisible("Q2").Should().BeFalse();
    }

    // The page's gate is answered "yes" but is itself hidden by an earlier question.
    [Fact]
    public void IsPageVisible_GateHiddenByItsOwnRule_OnlyTransitiveHidesThePage()
    {
        var page = new VisibilityRule("GATE", ["yes"]);
        var questions = new[] { Node("ROOT"), Node("GATE", "ROOT", "yes"), Node("Q1", page: page) };
        var answers = Answers(("ROOT", "no"), ("GATE", "yes"));

        var oneLevel = Visibility(transitive: false, answers, questions);
        oneLevel.IsPageVisible(page).Should().BeTrue();
        oneLevel.IsVisible("Q1").Should().BeTrue();

        var transitive = Visibility(transitive: true, answers, questions);
        transitive.IsPageVisible(page).Should().BeFalse();
        transitive.IsVisible("Q1").Should().BeFalse();
    }

    // A page save judges its questions among that page's only, so a parent elsewhere is not in the set.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IsVisible_ParentOutsideTheSet_IsJudgedOnItsAnswerAlone(bool transitive)
    {
        var questions = new[] { Node("CHILD", "ELSEWHERE", "yes") };

        Visibility(transitive, Answers(("ELSEWHERE", "yes")), questions).IsVisible("CHILD").Should().BeTrue();
        Visibility(transitive, Answers(("ELSEWHERE", "no")), questions).IsVisible("CHILD").Should().BeFalse();
    }

    // Two questions that reveal each other: the client's pruneHiddenAnswers keeps both while both rules hold, and so
    // does the transitive check. The rule guard (spike item 8) will refuse such a rule.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IsVisible_CycleWhoseRulesHold_StaysVisible(bool transitive)
    {
        var visibility = Visibility(transitive, Answers(("A", "y"), ("B", "x")), Node("A", "B", "x"), Node("B", "A", "y"));

        visibility.IsVisible("A").Should().BeTrue();
        visibility.IsVisible("B").Should().BeTrue();
    }

    // Once one rule of the cycle fails, its question's answer goes, and with it the other's reveal.
    [Fact]
    public void IsVisible_CycleWithARuleThatFails_OnlyTransitiveHidesBoth()
    {
        var questions = new[] { Node("A", "B", "x"), Node("B", "A", "y") };
        var answers = Answers(("B", "x"));

        var oneLevel = Visibility(transitive: false, answers, questions);
        oneLevel.IsVisible("A").Should().BeTrue();
        oneLevel.IsVisible("B").Should().BeFalse();

        var transitive = Visibility(transitive: true, answers, questions);
        transitive.IsVisible("A").Should().BeFalse();
        transitive.IsVisible("B").Should().BeFalse();
    }
}