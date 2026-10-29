using Application.Submissions.Protocol;
using Domain.KeyResults;
using FluentAssertions;
using Infrastructure.Persistence.Seed;
using Xunit;

namespace Application.Tests.Unit.Seed;

// Consistency checks over the static transcription of the Question Protocol's Results table. These guard the
// seed data against transcription slips (duplicate codes, dangling key-result or conditional references,
// option-less list questions, unbounded percentages) that would otherwise only surface at runtime.
public class KeyResultProtocolSeedDataTests
{
    // The option value every "Other" choice uses, in both shared option lists.
    private const string OtherOptionValue = "other";

    // The protocol covers every activity type except the four "Other type of …" rows and the People-and-roles
    // category (3 July update, BLUM-6933): 2 needs assessment + 3 strategy + 4 licensing + 5 education
    // + 3 help = 17.
    [Fact]
    public void Protocol_CoversTheSeventeenProtocolActivityTypes()
    {
        KeyResultProtocolSeedData.Protocol.Should().HaveCount(17);
    }

    [Fact]
    public void Protocol_TaskNamesAreUnique()
    {
        var names = KeyResultProtocolSeedData.Protocol.Select(t => t.TaskName).ToList();

        names.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void KeyResultCodes_AreUniquePerType()
    {
        foreach (var type in KeyResultProtocolSeedData.Protocol)
        {
            type.KeyResults.Select(k => k.Code).Should().OnlyHaveUniqueItems(
                "key-result codes must be unique within type \"{0}\"", type.TaskName);
        }
    }

    [Fact]
    public void QuestionCodeAndStatusScopePairs_AreUniquePerType()
    {
        foreach (var type in KeyResultProtocolSeedData.Protocol)
        {
            type.Questions.Select(q => (q.Code, q.StatusScope)).Should().OnlyHaveUniqueItems(
                "(Code, StatusScope) pairs must be unique within type \"{0}\"", type.TaskName);
        }
    }

    [Fact]
    public void EveryQuestion_ReferencesAKeyResultOfItsOwnType()
    {
        foreach (var type in KeyResultProtocolSeedData.Protocol)
        {
            var keyResultCodes = type.KeyResults.Select(k => k.Code).ToHashSet(StringComparer.Ordinal);

            foreach (var question in type.Questions)
            {
                keyResultCodes.Should().Contain(
                    question.KeyResultCode,
                    "question \"{0}\" of type \"{1}\" must reference a key result of that type",
                    question.Code,
                    type.TaskName);
            }
        }
    }

    [Fact]
    public void EveryConditionalQuestionCode_ReferencesAnotherQuestionOfTheSameType()
    {
        foreach (var type in KeyResultProtocolSeedData.Protocol)
        {
            var questionCodes = type.Questions.Select(q => q.Code).ToHashSet(StringComparer.Ordinal);

            foreach (var question in type.Questions.Where(q => q.ConditionalQuestionCode is not null))
            {
                question.ConditionalQuestionCode.Should().NotBe(
                    question.Code,
                    "question \"{0}\" of type \"{1}\" must not be conditional on itself",
                    question.Code,
                    type.TaskName);
                questionCodes.Should().Contain(
                    question.ConditionalQuestionCode,
                    "the conditional parent of question \"{0}\" of type \"{1}\" must exist in the same type",
                    question.Code,
                    type.TaskName);
            }
        }
    }

    // List and Radio both render from the seeded option list — Boolean is the only choice type whose options the
    // resolver synthesises, so anything else that offers choices must bring its own or renders an empty control.
    [Fact]
    public void ChoiceQuestions_AlwaysCarryOptions()
    {
        foreach (var type in KeyResultProtocolSeedData.Protocol)
        {
            foreach (var question in type.Questions.Where(q => q.FieldType is ProtocolFieldTypes.List or ProtocolFieldTypes.Radio))
            {
                question.Options.Should().NotBeNullOrEmpty(
                    "{0} question \"{1}\" of type \"{2}\" must have options",
                    question.FieldType,
                    question.Code,
                    type.TaskName);
            }
        }
    }

    // BLUM-7472: an "Other" option with no free-text follow-up is unanswerable — SubmissionAnswerValidator holds
    // list answers to the seeded option values, so the user can tick "Other" and has nowhere to say what it was,
    // and the export renders the bare label. Every question offering "other" needs a conditional text companion
    // for each of its phrasing variants, because the variants are separate rows and the resolver only ever asks
    // the one matching the activity's status.
    [Fact]
    public void EveryQuestionOfferingAnOtherOption_HasAConditionalFreeTextCompanion()
    {
        foreach (var type in KeyResultProtocolSeedData.Protocol)
        {
            var offeringOther = type.Questions.Where(q => q.Options?.Any(o => o.Value == OtherOptionValue) == true);

            foreach (var parent in offeringOther)
            {
                var hasCompanion = type.Questions.Any(q =>
                    q.FieldType == ProtocolFieldTypes.Text
                    && q.StatusScope == parent.StatusScope
                    && q.ConditionalQuestionCode == parent.Code
                    && q.ConditionalValue is not null
                    && q.ConditionalValue
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Contains(OtherOptionValue));

                hasCompanion.Should().BeTrue(
                    "question \"{0}\" of type \"{1}\" offers an \"Other\" option, so its {2} variant needs a conditional free-text companion",
                    parent.Code,
                    type.TaskName,
                    parent.StatusScope);
            }
        }
    }

    // The Active and Closed/Finished rows of one question share a Code precisely so a saved answer survives an
    // activity status change — which only holds if both variants validate the same way. SCR_TOOL was the one
    // violator (boolean while Active, free text once Closed); making the Closed variant a checkbox list would have
    // turned that into a saved "yes" failing the option allow-list, a 422 with no way past it (BLUM-7475), so the
    // list got its own SCR_SERVICES code instead. This keeps the next one from being seeded.
    [Fact]
    public void EveryQuestionCode_HasOneFieldTypeAcrossItsStatusVariants()
    {
        foreach (var type in KeyResultProtocolSeedData.Protocol)
        {
            foreach (var group in type.Questions.GroupBy(q => q.Code, StringComparer.Ordinal))
            {
                group.Select(q => q.FieldType).Distinct(StringComparer.Ordinal).Should().ContainSingle(
                    "question \"{0}\" of type \"{1}\" must use one field type across its status variants",
                    group.Key,
                    type.TaskName);
            }
        }
    }

    // BLUM-7645: nothing validates PrefillSource at runtime. KeyResultQuestionResolver.BuildPrefill matches it
    // against two constants and falls through a `_ => null` arm otherwise, so a typo prefills nothing and reads
    // exactly like the rows that decline a prefill on purpose (TRN_SERVICE_AREAS). The shapes have to line up too:
    // a who_with source hands over a comma-joined list of WhoWithOptions values, and SubmissionAnswerValidator then
    // holds that value to the question's OWN options — so a different option list would prefill something the LA
    // cannot complete the report with. who_with_other hands over free text, which only a Text question can hold.
    [Fact]
    public void EveryPrefillSource_IsRecognisedAndFitsItsQuestion()
    {
        var whoWithValues = KeyResultProtocolSeedData.WhoWithOptions.Select(o => o.Value).ToList();
        var known = new[] { KeyResultQuestionResolver.WhoWithPrefillSource, KeyResultQuestionResolver.WhoWithOtherPrefillSource };

        foreach (var type in KeyResultProtocolSeedData.Protocol)
        {
            foreach (var question in type.Questions.Where(q => q.PrefillSource is not null))
            {
                question.PrefillSource.Should().BeOneOf(
                    known,
                    "question \"{0}\" of type \"{1}\" names a prefill source the resolver does not recognise",
                    question.Code,
                    type.TaskName);

                if (question.PrefillSource == KeyResultQuestionResolver.WhoWithPrefillSource)
                {
                    question.FieldType.Should().Be(
                        ProtocolFieldTypes.List,
                        "question \"{0}\" of type \"{1}\" prefills a multi-select answer, which only a List question can hold",
                        question.Code,
                        type.TaskName);

                    question.Options.Should().NotBeNull(
                        "question \"{0}\" of type \"{1}\" prefills a list of choices, so it must carry the options to match them against",
                        question.Code,
                        type.TaskName);

                    question.Options!.Select(o => o.Value).Should().BeEquivalentTo(
                        whoWithValues,
                        "question \"{0}\" of type \"{1}\" prefills from the activity's \"Who will you work with?\" answer, so it must offer exactly those options",
                        question.Code,
                        type.TaskName);
                }
                else
                {
                    question.FieldType.Should().Be(
                        ProtocolFieldTypes.Text,
                        "question \"{0}\" of type \"{1}\" prefills free text, which only a Text question can hold",
                        question.Code,
                        type.TaskName);

                    // The pair only stays in step if the reveal hangs off the list that prefills alongside it: a
                    // who_with_other on an orphan or on a parent with no prefill puts the LA's "Other" text on
                    // screen under a list that has forgotten why "Other" was ticked.
                    type.Questions.Should().Contain(
                        q => q.Code == question.ConditionalQuestionCode
                            && q.StatusScope == question.StatusScope
                            && q.PrefillSource == KeyResultQuestionResolver.WhoWithPrefillSource,
                        "question \"{0}\" of type \"{1}\" prefills the \"Other\" free text, so its {2} parent must prefill the list it reveals from",
                        question.Code,
                        type.TaskName,
                        question.StatusScope);
                }
            }
        }
    }

    // BLUM-7697 split one merged health option into three and added "Affected others", so the array grew by four
    // in one edit. Two invariants make that kind of edit safe. Uniqueness is the sharp one: SeedFormDesigner turns
    // the array into form_question_options rows, and ApplicationDbContextInitialiser rethrows a 23505 raised while
    // the schema is still empty — a duplicate value is a permanently crash-looping boot, not a soft error.
    // "Other" closing the list is the GDS rule, and it is also the value the _WHO_WITH_OTHER reveal keys on.
    [Fact]
    public void WhoWithOptions_HaveUniqueValuesAndEndWithOther()
    {
        var options = KeyResultProtocolSeedData.WhoWithOptions;

        options.Select(o => o.Value).Should().OnlyHaveUniqueItems();
        options.Select(o => o.Label).Should().OnlyHaveUniqueItems();
        options[^1].Value.Should().Be(OtherOptionValue, "\"Other\" always closes a GDS option list");
        options.Take(options.Length - 1).Should().NotContain(
            o => o.Value == OtherOptionValue, "only the closing choice may be \"Other\"");
    }

    [Fact]
    public void PercentageQuestions_AreBoundedZeroToOneHundred()
    {
        foreach (var type in KeyResultProtocolSeedData.Protocol)
        {
            foreach (var question in type.Questions.Where(q => q.FieldType == ProtocolFieldTypes.Percentage))
            {
                question.Min.Should().Be(
                    0, "percentage question \"{0}\" of type \"{1}\" must have Min 0", question.Code, type.TaskName);
                question.Max.Should().Be(
                    100, "percentage question \"{0}\" of type \"{1}\" must have Max 100", question.Code, type.TaskName);
            }
        }
    }

    // BLUM-7790: DHSC withdrew the "Meeting attendance" key result and the "Who attended each meeting?" question
    // it unlocked, both transcribed from the 13 August sheet by BLUM-7620. Nothing else in the seed refers to
    // either, so re-adding them from that sheet on the next re-transcription would look like a correction rather
    // than a regression. Pinned here (rather than only in the integration test) because a unit assertion costs no
    // PostgreSQL container, and because it names the withdrawal explicitly for whoever reads the sheet next.
    [Fact]
    public void SteeringGroup_OffersOnlyTheMembersKeyResult_AndNoAttendanceQuestion()
    {
        var steeringGroup = KeyResultProtocolSeedData.Protocol
            .Should().ContainSingle(t => t.TaskName == "Setting up and running a local steering group").Subject;

        // Equal(collection, because, ...) rather than the params overload — the latter reads a reason string as
        // one more expected element.
        steeringGroup.KeyResults.Select(k => k.Code).Should().Equal(
            ["lsg_members"], "BLUM-7790 withdrew the \"Meeting attendance\" result ([B] on the measures screen)");
        steeringGroup.Questions.Should().OnlyContain(
            q => q.KeyResultCode == "lsg_members", "every steering-group question now hangs off the members result");

        KeyResultProtocolSeedData.Protocol
            .SelectMany(t => t.Questions)
            .Should().NotContain(
                q => q.Code == "LSG_ATTENDANCE", "BLUM-7790 withdrew the \"Who attended each meeting?\" question");
        KeyResultProtocolSeedData.Protocol
            .SelectMany(t => t.KeyResults)
            .Should().NotContain(q => q.Code == "lsg_attendance", "its key result went with it");
    }

    // BLUM-7788: DHSC's protocol asks "What is their level of engagement?" once per steering-group member, so the
    // question is seeded once per member option of LSG_MEMBERS and revealed by that member being ticked — replacing
    // the single LSG_ENGAGEMENT radio BLUM-7620 substituted for the multi-row control. Pinned here because the
    // fan-out is generated from WhoWithOptions: a slip in the generator (every row carrying one member's value, the
    // label losing the member, the codes colliding) produces 23 questions that each still look plausible alone.
    [Fact]
    public void SteeringGroupEngagement_IsAskedOncePerMember_ConditionalOnThatMemberBeingSelected()
    {
        var steeringGroup = KeyResultProtocolSeedData.Protocol
            .Should().ContainSingle(t => t.TaskName == "Setting up and running a local steering group").Subject;

        var engagement = steeringGroup.Questions
            .Where(q => q.Code.StartsWith("LSG_ENGAGEMENT_", StringComparison.Ordinal))
            .ToList();

        engagement.Select(q => q.ConditionalValue).Should().Equal(
            KeyResultProtocolSeedData.WhoWithOptions.Select(o => (string?)o.Value),
            "every member LSG_MEMBERS offers gets its own engagement question, in the same order");
        steeringGroup.Questions.Should().NotContain(
            q => q.Code == "LSG_ENGAGEMENT",
            "the single-radio substitution BLUM-7620 flagged back to DHSC is replaced, not kept alongside");

        foreach (var (question, member) in engagement.Zip(KeyResultProtocolSeedData.WhoWithOptions))
        {
            question.Code.Should().Be($"LSG_ENGAGEMENT_{member.Value.ToUpperInvariant()}");
            question.KeyResultCode.Should().Be("lsg_members");
            question.StatusScope.Should().Be(QuestionStatusScopes.Active);
            question.FieldType.Should().Be(ProtocolFieldTypes.Radio);
            question.ConditionalQuestionCode.Should().Be("LSG_MEMBERS");

            // The member is in the label and the error because check-your-answers rows, both activity-results.csv
            // exports and the SummaryList row testids key on the label, and an error-summary link has to say which
            // member it is pointing at.
            question.Label.Should().Be($"What is their level of engagement? ({member.Label})");
            question.ErrorMessage.Should().Be($"Select the level of engagement of {member.Label}");

            question.Options!.Select(o => o.Value).Should().Equal(
                ["attends_engages", "attends_limited", "contacted_no_response", "no_engagement", "do_not_know"],
                "the five engagement levels are shared by every member's question");
        }

        engagement.Select(q => q.Options).Should().OnlyContain(
            options => options!.Count == 5, "each member's question offers the five engagement levels");
    }

    // BLUM-7791: the "Health and Wellbeing Board" pair, transcribed from a later revision of the 13 August sheet
    // (Results table rows 20-21). Two things are pinned here.
    //
    // First the copy — the earlier sheet left both rows' field-type and error columns blank, so BLUM-7620 judged
    // them Boolean and worded the errors from the questions; the revision fills those columns in, and a re-read of
    // the older file would otherwise reinstate the invented strings without anything failing.
    //
    // Second, and more importantly, the SHAPE of the rule: one Code under two StatusScopes, hanging off the
    // sap_hwb_presented key result, with no ConditionalQuestionCode. That is the whole of "show it only when
    // result [B] was selected and only in the phrasing matching the activity's status" — KeyResultQuestionResolver
    // asks for a question only when its key result was selected and its scope matches. Reaching for the
    // ConditionalQuestionCode/ConditionalValue pair instead is the plausible wrong move, and it cannot work: that
    // pair is evaluated against the ANSWERS dictionary by all four ports of IsVisible, and an activity's status is
    // not an answer, so the questions would silently become unconditional. Sharing one Code is what makes a yes/no
    // saved while Active the value the Closed/Finished phrasing re-opens ("pull 'Active' text for final edit").
    [Fact]
    public void HealthAndWellbeingBoard_IsAskedOncePerStatusPhrasing_OffTheBoardKeyResult()
    {
        var strategy = KeyResultProtocolSeedData.Protocol
            .Should().ContainSingle(t => t.TaskName == "Documenting a strategy and action plan").Subject;

        strategy.KeyResults.Should().ContainSingle(k => k.Code == "sap_hwb_presented")
            .Which.Label.Should().Be("Work on gambling harms presented to the Health and Wellbeing Board");

        var board = strategy.Questions.Where(q => q.KeyResultCode == "sap_hwb_presented").ToList();

        board.Should().OnlyContain(
            q => q.Code == "SAP_HWB_PRESENTED", "both phrasings share one answer key so a saved yes/no survives a status change");
        board.Should().OnlyContain(
            q => q.FieldType == ProtocolFieldTypes.Boolean, "the revised sheet fills in the field type BLUM-7620 had to judge");
        board.Should().OnlyContain(
            q => q.ConditionalQuestionCode == null && q.ConditionalValue == null,
            "the rule is KeyResultCode + StatusScope; the conditional pair reads answers and cannot see the activity's status");

        var active = board.Should().ContainSingle(q => q.StatusScope == QuestionStatusScopes.Active).Subject;
        active.Label.Should().Be("Will you present work on gambling harms prevention to the Health and Wellbeing Board?");
        active.ErrorMessage.Should().Be("Select yes if you are planning on presenting to the Health and Wellbeing Board");

        var closedFinished = board.Should().ContainSingle(q => q.StatusScope == QuestionStatusScopes.ClosedFinished).Subject;
        closedFinished.Label.Should().Be("Have you presented work on gambling harms prevention to the Health and Wellbeing Board?");
        closedFinished.ErrorMessage.Should().Be("Select yes if you have presented to the Health and Wellbeing Board");
    }

    // BLUM-7791, same screen: the sibling result [A]. The revision answers the contradiction BLUM-7620 raised —
    // the Closed/Finished error said "presented" against a question asking whether a draft had been developed —
    // and gives the Active label the "and/or action plan" wording its Closed/Finished twin already had. Pinned so
    // a re-read of the older sheet cannot quietly put the mismatch back as though it were the source of truth.
    [Fact]
    public void StrategyAndActionPlan_UsesTheRevisedWording_ForBothStatusPhrasings()
    {
        var strategy = KeyResultProtocolSeedData.Protocol
            .Should().ContainSingle(t => t.TaskName == "Documenting a strategy and action plan").Subject;

        var plan = strategy.Questions.Where(q => q.KeyResultCode == "sap_plan_developed").ToList();

        var active = plan.Should().ContainSingle(q => q.StatusScope == QuestionStatusScopes.Active).Subject;
        active.Label.Should().Be("Is a draft strategy and/or action plan being developed?");
        active.ErrorMessage.Should().Be("Select yes if a draft is being developed");

        var closedFinished = plan.Should().ContainSingle(q => q.StatusScope == QuestionStatusScopes.ClosedFinished).Subject;
        closedFinished.Label.Should().Be("Has a draft strategy and/or action plan been developed?");
        closedFinished.ErrorMessage.Should().Be(
            "Select yes if a draft has been developed", "DHSC corrected the \"presented\" mismatch BLUM-7620 flagged back to them");
    }

    // BLUM-8159: the data map lists the other strategies as Alcohol, Drugs, Mental health, Suicide, Other — the seed
    // stopped at Suicide. The four existing values are pinned alongside the new one because saved answers are held
    // to them by exact match, and the maturity-assessment prefill reads them 1:1 (MaturityProtocol.StrategyItems).
    [Fact]
    public void OtherStrategies_EndWithOther_RevealingAFreeTextCompanion()
    {
        var strategy = KeyResultProtocolSeedData.Protocol
            .Should().ContainSingle(t => t.TaskName == "Documenting a strategy and action plan").Subject;

        var strategies = strategy.Questions.Should().ContainSingle(q => q.Code == "SAP_OTHER_STRATEGIES").Subject;
        strategies.Options!.Select(o => o.Value).Should().Equal(
            ["alcohol", "drugs", "mental_health", "suicide", OtherOptionValue], "the data map's order, \"Other\" last");
        strategies.Options![^1].Label.Should().Be("Other");

        var companion = strategy.Questions.Should().ContainSingle(q => q.Code == "SAP_OTHER_STRATEGIES_OTHER").Subject;
        companion.KeyResultCode.Should().Be("sap_other_strategies");
        companion.StatusScope.Should().Be(QuestionStatusScopes.ClosedFinished, "its parent is only asked once Closed or Finished");
        companion.FieldType.Should().Be(ProtocolFieldTypes.Text);
        companion.ConditionalQuestionCode.Should().Be("SAP_OTHER_STRATEGIES");
        companion.ConditionalValue.Should().Be(OtherOptionValue);
        companion.Label.Should().NotBe(
            strategies.Label, "check-your-answers rows, the CSV exports and SummaryList row testids key on the label");
        companion.ErrorMessage.Should().Be("Enter the other strategies that have been updated");
    }

    [Fact]
    public void EveryStatusScopeAndFieldType_ParsesIntoItsValueObject()
    {
        foreach (var type in KeyResultProtocolSeedData.Protocol)
        {
            foreach (var question in type.Questions)
            {
                var parseScope = () => QuestionStatusScope.From(question.StatusScope);
                var parseFieldType = () => ProtocolFieldType.From(question.FieldType);

                parseScope.Should().NotThrow(
                    "the StatusScope of question \"{0}\" of type \"{1}\" must be supported", question.Code, type.TaskName);
                parseFieldType.Should().NotThrow(
                    "the FieldType of question \"{0}\" of type \"{1}\" must be supported", question.Code, type.TaskName);
            }
        }
    }
}