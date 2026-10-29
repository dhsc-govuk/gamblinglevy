using Application.Forms.Rules;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Unit.Forms;

// ShowHideRuleGuard is the one check that a form's show/hide rules are valid (BLUM-9087 spike item 8). Check judges a
// whole form, as publish will; CheckChange refuses only what an edit introduces, and says whether the edit's own rule
// is at fault (422 INVALID_CONDITIONAL), another question's rule depends on what it changed (409 QUESTION_REFERENCED),
// or it adds an option value holding a comma (422 OPTION_VALUE_HAS_COMMA).
public class ShowHideRuleGuardTests
{
    private static RuleQuestion Choice(string code, params string[] values) =>
        new(code, IsChoice: true, IsActive: true, values.Select(value => new RuleOption(value)).ToArray());

    private static RuleQuestion Text(string code, string? parent = null, string? values = null) =>
        new(code, IsChoice: false, IsActive: true, [], parent, values);

    private static RulePage Page(string key, params RuleQuestion[] questions) => new(key, questions);

    private static RuleForm Form(params RulePage[] pages) => new(pages);

    private static IReadOnlyList<RuleViolation> Check(params RulePage[] pages) => ShowHideRuleGuard.Check(Form(pages));

    private static RuleViolation SingleViolation(params RulePage[] pages) => Check(pages).Should().ContainSingle().Subject;

    [Theory]
    [InlineData("yes")]
    [InlineData("yes,no")]
    [InlineData(" yes , no ")]
    public void Check_ARuleOnAnEarlierChoiceQuestionOfThePage_IsValid(string values)
    {
        Check(Page("page", Choice("PARENT", "yes", "no"), Text("CHILD", "PARENT", values))).Should().BeEmpty();
    }

    [Fact]
    public void Check_AFormWithNoRules_IsValid()
    {
        Check(Page("one", Choice("RADIO", "yes", "no"), Text("TEXT")), Page("two", Text("MORE"))).Should().BeEmpty();
    }

    [Fact]
    public void Check_APageRuleOnAnEarlierPage_IsValid()
    {
        var form = Form(Page("one", Choice("PARENT", "yes", "no")), new RulePage("two", [Text("CHILD")], "PARENT", "yes"));

        ShowHideRuleGuard.Check(form).Should().BeEmpty();
    }

    [Theory]
    [InlineData(" ", "yes")]
    [InlineData(null, "yes")]
    [InlineData("PARENT", null)]
    [InlineData("PARENT", "  ")]
    public void Check_ARuleWithOneHalfBlank_IsAHalfSetRule(string? parent, string? values)
    {
        var violation = SingleViolation(Page("page", Choice("PARENT", "yes"), Text("CHILD", parent, values)));

        violation.Problem.Should().Be(RuleProblem.HalfSet);
        violation.Owner.Should().Be("CHILD");
        violation.OnPage.Should().BeFalse();
    }

    [Fact]
    public void Check_ARuleWhoseValueIsOnlyCommas_NamesNoValues()
    {
        SingleViolation(Page("page", Choice("PARENT", "yes"), Text("CHILD", "PARENT", " , ,"))).Problem.Should().Be(RuleProblem.NoValues);
    }

    [Theory]
    [InlineData("ELSEWHERE")]
    [InlineData("parent")]
    public void Check_ARuleOnACodeNotOnTheForm_HasAMissingParent(string parent)
    {
        var violation = SingleViolation(Page("page", Choice("PARENT", "yes"), Text("CHILD", parent, "yes")));

        violation.Problem.Should().Be(RuleProblem.ParentMissing);
        violation.RuleQuestionCode.Should().Be(parent);
    }

    [Fact]
    public void Check_ARuleOnTheQuestionItself_IsRefused()
    {
        var violation = SingleViolation(Page("page", Choice("SELF", "yes", "no") with { RuleQuestionCode = "SELF", RuleValue = "yes" }));

        violation.Problem.Should().Be(RuleProblem.ParentNotEarlier);
        violation.Message.Should().Be("\"SELF\" cannot depend on itself.");
    }

    [Fact]
    public void Check_ARuleOnAWithdrawnQuestion_HasAnInactiveParent()
    {
        var violation = SingleViolation(Page("page", Choice("PARENT", "yes") with { IsActive = false }, Text("CHILD", "PARENT", "yes")));

        violation.Problem.Should().Be(RuleProblem.ParentInactive);
    }

    [Fact]
    public void Check_ARuleOnATextQuestion_HasANonChoiceParent()
    {
        SingleViolation(Page("page", Text("PARENT"), Text("CHILD", "PARENT", "yes"))).Problem.Should().Be(RuleProblem.ParentNotChoice);
    }

    [Fact]
    public void Check_ARuleOnAQuestionOfAnotherPage_IsRefused_EvenAnEarlierPage()
    {
        var violation = SingleViolation(Page("one", Choice("PARENT", "yes")), Page("two", Text("CHILD", "PARENT", "yes")));

        violation.Problem.Should().Be(RuleProblem.ParentOnAnotherPage);
    }

    [Fact]
    public void Check_ARuleOnALaterQuestionOfThePage_IsRefused()
    {
        SingleViolation(Page("page", Text("CHILD", "PARENT", "yes"), Choice("PARENT", "yes"))).Problem.Should().Be(RuleProblem.ParentNotEarlier);
    }

    [Fact]
    public void Check_ATwoQuestionCycle_IsRefusedOnTheEarlierQuestion()
    {
        var first = Choice("FIRST", "yes", "no") with { RuleQuestionCode = "SECOND", RuleValue = "yes" };
        var second = Choice("SECOND", "yes", "no") with { RuleQuestionCode = "FIRST", RuleValue = "yes" };

        var violation = SingleViolation(Page("page", first, second));

        violation.Problem.Should().Be(RuleProblem.ParentNotEarlier);
        violation.Owner.Should().Be("FIRST");
    }

    [Theory]
    [InlineData("one")]
    [InlineData("two")]
    public void Check_APageRuleOnItsOwnPageOrALaterOne_IsRefused(string parentPage)
    {
        var parent = Choice("PARENT", "yes");
        var form = Form(
            new RulePage("one", parentPage == "one" ? [parent] : [Text("FILLER")], "PARENT", "yes"),
            Page("two", parentPage == "two" ? [parent] : [Text("FILLER")]));

        var violation = ShowHideRuleGuard.Check(form).Should().ContainSingle().Subject;

        violation.Problem.Should().Be(RuleProblem.ParentNotOnEarlierPage);
        violation.Owner.Should().Be("one");
        violation.OnPage.Should().BeTrue();
    }

    [Fact]
    public void Check_APageRule_IsJudgedLikeAQuestionsRule()
    {
        var form = Form(Page("one", Text("PARENT")), new RulePage("two", [Text("CHILD")], "PARENT", "yes"));

        var violation = ShowHideRuleGuard.Check(form).Should().ContainSingle().Subject;

        violation.Problem.Should().Be(RuleProblem.ParentNotChoice);
        violation.Message.Should().StartWith("The show/hide rule on page \"two\"");
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(null, "finished")]
    [InlineData("closed,finished", "finished")]
    [InlineData("active", "active")]
    public void Check_AParentAskedInEveryStatusTheChildIs_CoversIt(string? parentStatuses, string? childStatuses)
    {
        Check(Page("result", Choice("PARENT", "yes") with { Statuses = Statuses(parentStatuses) }, Text("CHILD", "PARENT", "yes") with { Statuses = Statuses(childStatuses) }))
            .Should().BeEmpty();
    }

    [Theory]
    [InlineData("active", null)]
    [InlineData("finished", "closed,finished")]
    [InlineData("active", "finished")]
    public void Check_AParentNotAskedInEveryStatusTheChildIs_DoesNotCoverIt(string parentStatuses, string? childStatuses)
    {
        var violation = SingleViolation(Page(
            "result",
            Choice("PARENT", "yes") with { Statuses = Statuses(parentStatuses) },
            Text("CHILD", "PARENT", "yes") with { Statuses = Statuses(childStatuses) }));

        violation.Problem.Should().Be(RuleProblem.ParentStatusesDoNotCover);
    }

    [Fact]
    public void Check_AValueTheParentDoesNotOffer_IsRefused_NamingThatValue()
    {
        var violation = SingleViolation(Page("page", Choice("PARENT", "yes", "no"), Text("CHILD", "PARENT", "yes,maybe")));

        violation.Problem.Should().Be(RuleProblem.ValueNotAnActiveOption);
        violation.Value.Should().Be("maybe");
    }

    [Fact]
    public void Check_AValueWhoseOptionIsWithdrawn_IsRefused()
    {
        var parent = new RuleQuestion("PARENT", IsChoice: true, IsActive: true, [new RuleOption("yes", IsActive: false), new RuleOption("no")]);

        var violation = SingleViolation(Page("page", parent, Text("CHILD", "PARENT", "yes")));

        violation.Problem.Should().Be(RuleProblem.ValueNotAnActiveOption);
        violation.Value.Should().Be("yes");
    }

    [Fact]
    public void Check_AnActiveOptionValueWithAComma_IsRefused_ButNotAWithdrawnOneOrAWithdrawnQuestions()
    {
        var withComma = new RuleQuestion("RADIO", IsChoice: true, IsActive: true, [new RuleOption("a,b"), new RuleOption("c,d", IsActive: false)]);
        var withdrawn = Choice("WITHDRAWN", "e,f") with { IsActive = false };

        var violation = SingleViolation(Page("page", withComma, withdrawn));

        violation.Problem.Should().Be(RuleProblem.OptionValueHasComma);
        violation.Owner.Should().Be("RADIO");
        violation.Value.Should().Be("a,b");
    }

    [Fact]
    public void Check_ListsEveryProblemWithARule_AndEachValueNotOffered()
    {
        var parent = Choice("PARENT", "yes") with { IsActive = false };

        var problems = Check(Page("one", parent), Page("two", Text("CHILD", "PARENT", "maybe,yes,never")));

        problems.Select(violation => (violation.Problem, violation.Value)).Should().Equal(
            (RuleProblem.ParentInactive, null),
            (RuleProblem.ParentOnAnotherPage, null),
            (RuleProblem.ValueNotAnActiveOption, "maybe"),
            (RuleProblem.ValueNotAnActiveOption, "never"));
    }

    [Fact]
    public void Check_AWithdrawnQuestionsRule_IsNotJudged()
    {
        Check(Page("page", Text("CHILD", "MISSING", "yes") with { IsActive = false })).Should().BeEmpty();
    }

    private static readonly RulePage[] Base =
    [
        Page("one", Choice("PARENT", "yes", "no"), Text("CHILD", "PARENT", "yes")),
        Page("two", Text("OTHER")),
    ];

    private static RuleGuardOutcome Change(RulePage[] after, string? changedQuestionCode = null, string? changedPageKey = null) =>
        ShowHideRuleGuard.CheckChange(Form(Base), Form(after), changedQuestionCode, changedPageKey);

    [Fact]
    public void CheckChange_AnEditThatBreaksNothing_IsAllowed()
    {
        var outcome = Change([Base[0], Page("two", Text("OTHER"), Choice("NEW", "x"))], "NEW");

        outcome.Should().Be(RuleGuardOutcome.Allowed);
        outcome.IsRefused.Should().BeFalse();
        outcome.Message.Should().BeNull();
    }

    [Fact]
    public void CheckChange_CreatingAQuestionWithABadRule_IsAnInvalidRule()
    {
        var outcome = Change([Page("one", Choice("PARENT", "yes", "no"), Text("CHILD", "PARENT", "yes"), Text("NEW", "MISSING", "yes")), Base[1]], "NEW");

        outcome.Refusal.Should().Be(RuleRefusal.InvalidRule);
        outcome.Violation!.Problem.Should().Be(RuleProblem.ParentMissing);
        outcome.Message.Should().Be(outcome.Violation.Message);
    }

    [Theory]
    [InlineData("delete", RuleProblem.ParentMissing)]
    [InlineData("withdraw", RuleProblem.ParentInactive)]
    [InlineData("retype", RuleProblem.ParentNotChoice)]
    [InlineData("move page", RuleProblem.ParentOnAnotherPage)]
    [InlineData("withdraw option", RuleProblem.ValueNotAnActiveOption)]
    [InlineData("remove option", RuleProblem.ValueNotAnActiveOption)]
    [InlineData("reorder", RuleProblem.ParentNotEarlier)]
    public void CheckChange_ChangingAParentAnotherRuleUses_IsReferenced(string change, RuleProblem problem)
    {
        var parent = Choice("PARENT", "yes", "no");
        var child = Text("CHILD", "PARENT", "yes");
        RulePage[] after = change switch
        {
            "delete" => [Page("one", child), Base[1]],
            "withdraw" => [Page("one", parent with { IsActive = false }, child), Base[1]],
            "retype" => [Page("one", parent with { IsChoice = false, Options = [] }, child), Base[1]],
            "move page" => [Page("one", child), Page("two", Text("OTHER"), parent)],
            "withdraw option" => [Page("one", parent with { Options = [new RuleOption("yes", IsActive: false), new RuleOption("no")] }, child), Base[1]],
            "remove option" => [Page("one", Choice("PARENT", "no"), child), Base[1]],
            "reorder" => [Page("one", child, parent), Base[1]],
            _ => throw new ArgumentOutOfRangeException(nameof(change)),
        };

        var outcome = Change(after, change == "reorder" ? null : "PARENT");

        outcome.Refusal.Should().Be(RuleRefusal.Referenced);
        outcome.Violation!.Problem.Should().Be(problem);
        outcome.Violation.Owner.Should().Be("CHILD");
        outcome.Violation.RuleQuestionCode.Should().Be("PARENT");
        outcome.Message.Should().Be($"{outcome.Violation.Message} Change or remove that rule first.");
    }

    [Fact]
    public void CheckChange_MovingAChildAwayFromItsParent_IsAnInvalidRule()
    {
        var outcome = Change([Page("one", Choice("PARENT", "yes", "no")), Page("two", Text("OTHER"), Text("CHILD", "PARENT", "yes"))], "CHILD");

        outcome.Refusal.Should().Be(RuleRefusal.InvalidRule);
        outcome.Violation!.Problem.Should().Be(RuleProblem.ParentOnAnotherPage);
    }

    [Fact]
    public void CheckChange_AnEditWhoseOwnRuleIsBadAndThatBreaksAnother_IsAnInvalidRuleFirst()
    {
        var parent = Text("PARENT", "MISSING", "yes");

        var outcome = Change([Page("one", parent, Text("CHILD", "PARENT", "yes")), Base[1]], "PARENT");

        outcome.Refusal.Should().Be(RuleRefusal.InvalidRule);
        outcome.Violation!.Owner.Should().Be("PARENT");
    }

    [Fact]
    public void CheckChange_SettingABadPageRule_IsAnInvalidRule_AndBreakingOneIsReferenced()
    {
        var withPageRule = new RulePage("two", [Text("OTHER")], "PARENT", "yes");

        var setting = Change([Base[0], withPageRule with { RuleValue = "maybe" }], changedPageKey: "two");
        setting.Refusal.Should().Be(RuleRefusal.InvalidRule);
        setting.Violation!.OnPage.Should().BeTrue();

        var breaking = ShowHideRuleGuard.CheckChange(
            Form(Base[0], withPageRule),
            Form(Page("one", Text("PARENT"), Text("CHILD", "PARENT", "yes")), withPageRule),
            "PARENT");
        breaking.Refusal.Should().Be(RuleRefusal.Referenced);
    }

    [Fact]
    public void CheckChange_AddingAnOptionValueWithAComma_IsRefusedBeforeABadRule()
    {
        var newQuestion = Choice("NEW", "a,b") with { RuleQuestionCode = "MISSING", RuleValue = "yes" };

        var outcome = Change([Page("one", Choice("PARENT", "yes", "no"), Text("CHILD", "PARENT", "yes"), newQuestion), Base[1]], "NEW");

        outcome.Refusal.Should().Be(RuleRefusal.OptionValueHasComma);
        outcome.Violation!.Value.Should().Be("a,b");
    }

    [Fact]
    public void CheckChange_AnOptionValueWithACommaAlreadyThere_DoesNotBlockAnEdit_ButANewOneIs()
    {
        RulePage[] before = [Page("page", Choice("RADIO", "a,b", "c"))];

        ShowHideRuleGuard.CheckChange(Form(before), Form(before), "RADIO").Should().Be(RuleGuardOutcome.Allowed);

        var adding = ShowHideRuleGuard.CheckChange(Form(before), Form(Page("page", Choice("RADIO", "a,b", "c", "d,e"))), "RADIO");
        adding.Refusal.Should().Be(RuleRefusal.OptionValueHasComma);
        adding.Violation!.Value.Should().Be("d,e");
    }

    [Fact]
    public void CheckChange_ARuleThatWasAlreadyInvalid_DoesNotBlockAnEdit_UnlessTheEditRewritesIt()
    {
        // The activity record editor accepted a parent anywhere in the form, so a saved rule may already break these.
        RulePage[] before = [Page("one", Choice("PARENT", "yes", "no")), Page("two", Text("LEGACY", "PARENT", "yes"))];

        ShowHideRuleGuard.CheckChange(Form(before), Form(before), "LEGACY").Should().Be(RuleGuardOutcome.Allowed);
        ShowHideRuleGuard.CheckChange(Form(before), Form(before[0], Page("two", Text("LEGACY", "PARENT", "yes"), Text("NEW"))), "NEW")
            .Should().Be(RuleGuardOutcome.Allowed);

        var rewritten = ShowHideRuleGuard.CheckChange(Form(before), Form(before[0], Page("two", Text("LEGACY", "PARENT", "no"))), "LEGACY");
        rewritten.Refusal.Should().Be(RuleRefusal.InvalidRule);
        rewritten.Violation!.Problem.Should().Be(RuleProblem.ParentOnAnotherPage);
    }

    [Fact]
    public void CheckChange_AProblemAddedToARuleThatWasAlreadyInvalid_IsStillReferenced()
    {
        RulePage[] before = [Page("one", Choice("PARENT", "yes", "no")), Page("two", Text("LEGACY", "PARENT", "yes"))];
        var withdrawn = new RuleQuestion("PARENT", IsChoice: true, IsActive: true, [new RuleOption("yes", IsActive: false), new RuleOption("no")]);

        var outcome = ShowHideRuleGuard.CheckChange(Form(before), Form(Page("one", withdrawn), before[1]), "PARENT");

        outcome.Refusal.Should().Be(RuleRefusal.Referenced);
        outcome.Violation!.Problem.Should().Be(RuleProblem.ValueNotAnActiveOption);
        outcome.Violation.Owner.Should().Be("LEGACY");
    }

    [Theory]
    [InlineData("no,yes")]
    [InlineData(" yes , no ")]
    [InlineData("yes,no,yes")]
    public void CheckChange_ARuleThatWasAlreadyInvalid_SentBackInAnotherOrderOrSpacing_IsTheSameRule(string resent)
    {
        RulePage[] before = [Page("one", Choice("PARENT", "yes", "no")), Page("two", Text("LEGACY", "PARENT", "yes,no"))];

        var outcome = ShowHideRuleGuard.CheckChange(Form(before), Form(before[0], Page("two", Text("LEGACY", "PARENT", resent))), "LEGACY");

        outcome.Should().Be(RuleGuardOutcome.Allowed);
    }

    [Fact]
    public void CheckChange_ACommaOnAQuestionTheChangeDoesNotName_ComesAfterTheChangesOwnRule()
    {
        RulePage[] before = [Page("one", Choice("PARENT", "yes"), Choice("HIDDEN", "a,b") with { IsActive = false })];
        RulePage[] after = [Page("one", Choice("PARENT", "yes"), Choice("HIDDEN", "a,b"), Text("NEW", "MISSING", "yes"))];

        var outcome = ShowHideRuleGuard.CheckChange(Form(before), Form(after), "NEW");
        outcome.Refusal.Should().Be(RuleRefusal.InvalidRule);
        outcome.Violation!.Owner.Should().Be("NEW");

        var reactivating = ShowHideRuleGuard.CheckChange(Form(before), Form(after[0] with { Questions = after[0].Questions.Take(2).ToArray() }));
        reactivating.Refusal.Should().Be(RuleRefusal.OptionValueHasComma);
        reactivating.Violation!.Owner.Should().Be("HIDDEN");
    }

    private static IReadOnlySet<string>? Statuses(string? statuses) =>
        statuses?.Split(',').ToHashSet(StringComparer.Ordinal);
}