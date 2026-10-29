using Application.MaturityAssessments.Export;
using Application.MaturityAssessments.Protocol;
using FluentAssertions;
using Infrastructure.Persistence.Seed;
using Xunit;

namespace Application.Tests.Unit.MaturityAssessments;

// Consistency checks over the static maturity-assessment catalogue (BLUM-7855): the transcription slips that
// would otherwise only surface as a 422 on a page the LA cannot fix — dangling conditionals, an "Other" with no
// text companion, a matrix missing an item — and the three option lists the quarterly prefill copies values from
// 1:1, pinned against the quarterly seed so the two cannot drift apart silently.
public class MaturityProtocolTests
{
    private static readonly IReadOnlyList<MaturityResolvedQuestion> All = MaturityQuestionResolver.ResolveAll();

    [Fact]
    public void Sections_AreTheTenMappingFileDomainsInOrder()
    {
        MaturityProtocol.SectionCodes.Should().Equal(
            "la_capacity",
            "needs_assessment",
            "strategic_approach",
            "awareness_training_signposting",
            "planning_licensing",
            "local_campaigns",
            "advertising_restrictions",
            "working_with_vcse",
            "other_preventative_services",
            "next_steps");
    }

    [Fact]
    public void QuestionCodes_AreUniqueAndPrefixed()
    {
        All.Select(q => q.Code).Should().OnlyHaveUniqueItems();
        All.Should().OnlyContain(q => q.Code.StartsWith("MA_"));
    }

    // The client's route templates reserve these segments, so a section or page named after one would be
    // unreachable.
    [Fact]
    public void SectionAndPageCodes_NeverCollideWithTheReservedRouteSegments()
    {
        MaturityProtocol.SectionCodes.Should().NotContain(["submit", "submitted"]);
        MaturityProtocol.Sections.SelectMany(s => s.Pages).Select(p => p.Code).Should().NotContain("check");

        foreach (var section in MaturityProtocol.Sections)
        {
            section.Pages.Select(p => p.Code).Should().OnlyHaveUniqueItems("pages within {0} must be distinct", section.Code);
        }
    }

    [Fact]
    public void ConditionalParents_ExistAndComeEarlier()
    {
        var index = All.Select((q, i) => (q.Code, i)).ToDictionary(x => x.Code, x => x.i);

        foreach (var question in All.Where(q => q.ConditionalQuestionCode is not null))
        {
            index.Should().ContainKey(question.ConditionalQuestionCode!, "{0} points at a parent", question.Code);
            index[question.ConditionalQuestionCode!].Should().BeLessThan(index[question.Code]);

            var parent = All.Single(q => q.Code == question.ConditionalQuestionCode);
            foreach (var value in question.ConditionalValue!.Split(','))
            {
                parent.Options.Should().Contain(o => o.Value == value, "{0} reveals on a value {1} offers", question.Code, parent.Code);
            }
        }

        foreach (var page in MaturityProtocol.Sections.SelectMany(s => s.Pages).Where(p => p.VisibleWhen is not null))
        {
            var parent = All.Single(q => q.Code == page.VisibleWhen!.QuestionCode);
            parent.Options.Select(o => o.Value).Should().Contain(page.VisibleWhen!.Values);
        }
    }

    // A collection can leave any section off (BLUM-8093), so no question or page may depend on an answer in ANOTHER
    // section: an excluded parent would never be answered and its child could never be revealed — or, for a page
    // rule, the whole page would silently vanish.
    [Fact]
    public void ConditionalParents_AreInTheSameSection()
    {
        var sectionByCode = All.ToDictionary(q => q.Code, q => q.Section);

        foreach (var question in All.Where(q => q.ConditionalQuestionCode is not null))
        {
            sectionByCode[question.ConditionalQuestionCode!].Should().Be(question.Section, "{0} depends on its parent", question.Code);
        }

        foreach (var section in MaturityProtocol.Sections)
        {
            foreach (var page in section.Pages.Where(p => p.VisibleWhen is not null))
            {
                sectionByCode[page.VisibleWhen!.QuestionCode].Should().Be(section.Code, "page {0} depends on its parent", page.Code);
            }
        }
    }

    [Fact]
    public void ChoiceQuestions_HaveOptions_AndNoQuestionHasACharacterLimit()
    {
        foreach (var question in All.Where(q => MaturityFieldTypes.IsChoice(q.QuestionType)))
        {
            question.Options.Should().NotBeEmpty("{0} is a {1}", question.Code, question.QuestionType);
            question.Options.Select(o => o.Value).Should().OnlyHaveUniqueItems();
            question.Options.Should().OnlyContain(o => !o.Value.Contains(','), "multi-select values are comma-joined");
        }

        // BLUM-8411: MaxLength is what drives both the length check and the client's GDS character count, so a
        // limit on any question — free text or not — would bring back an error or a counter the ticket removed.
        All.Where(q => MaturityFieldTypes.IsFreeText(q.QuestionType)).Should().NotBeEmpty();

        foreach (var question in All)
        {
            question.MaxLength.Should().BeNull("{0} has no character limit", question.Code);
            question.MaxLengthErrorMessage.Should().BeNull("{0} has no character limit", question.Code);
        }
    }

    [Fact]
    public void EveryOtherOption_HasAFreeTextCompanionRevealedByIt()
    {
        // The funded-roles repeater carries its own inline "other role" text per row instead of a companion question.
        var withOther = All.Where(q => q.Options.Any(o => o.Value == MaturityOptionValues.Other) && q.GroupLabel is null && !q.IsRoles).ToList();
        withOther.Should().NotBeEmpty();

        foreach (var question in withOther)
        {
            var companion = All.SingleOrDefault(q => q.Code == $"{question.Code}_OTHER");
            companion.Should().NotBeNull("{0} offers Other", question.Code);
            companion!.ConditionalQuestionCode.Should().Be(question.Code);
            companion.ConditionalValue.Should().Be(MaturityOptionValues.Other);
            companion.QuestionType.Should().Be(MaturityFieldTypes.Textarea);
        }
    }

    [Fact]
    public void ExactlyOneRolesQuestion_WithBothOptionLists()
    {
        var roles = All.Where(q => q.IsRoles).ToList();

        roles.Should().ContainSingle().Which.Code.Should().Be("MA_FUNDED_ROLES");
        roles[0].Options.Should().BeEquivalentTo(MaturityProtocol.FundedRoleOptions);
        roles[0].SecondaryOptions.Should().BeEquivalentTo(MaturityProtocol.FundedTimeOptions);
    }

    // BLUM-9112: the mapping file's funded-roles answers are the six roles, "Other" and "None".
    [Fact]
    public void FundedRoleOptions_AreTheRolesThenOtherThenTheExclusiveNone()
    {
        MaturityProtocol.FundedRoleOptions.Select(o => o.Value).Should().Equal(
            [.. MaturityProtocol.RoleOptions.Select(o => o.Value), MaturityOptionValues.Other, MaturityOptionValues.None]);
        MaturityProtocol.FundedRoleOptions[^1].Label.Should().Be("None");
        All.Single(q => q.Code == "MA_FUNDED_ROLES").ExclusiveOptionValue.Should().Be(MaturityOptionValues.None);
    }

    // An exclusive value the question does not offer could never be chosen, and the validator would refuse it anyway.
    [Fact]
    public void EveryExclusiveOptionValue_IsOneOfItsQuestionsOptions()
    {
        var exclusive = All.Where(q => q.ExclusiveOptionValue is not null).ToList();

        exclusive.Should().NotBeEmpty();
        exclusive.Should().OnlyContain(q => q.Options.Any(o => o.Value == q.ExclusiveOptionValue));
    }

    [Theory]
    [InlineData("MA_NA_ENGAGEMENT", 23)]
    [InlineData("MA_SG_MEMBERS", 23)]
    [InlineData("MA_STRATEGIES", 4)]
    [InlineData("MA_TRAINED_WORKFORCES", 23)]
    [InlineData("MA_SCREENING", 23)]
    public void Matrices_GenerateOneRadioPerItem(string prefix, int expectedItems)
    {
        var items = All.Where(q => q.Code.StartsWith(prefix + "__", StringComparison.Ordinal)).ToList();

        items.Should().HaveCount(expectedItems);
        items.Should().OnlyContain(q => q.QuestionType == MaturityFieldTypes.Radio && q.GroupLabel != null);
        items.Select(q => q.GroupLabel).Distinct().Should().ContainSingle();
    }

    // The quarterly prefill copies these values across untranslated (BLUM-7856), so the lists must stay equal to
    // what the quarterly report stores.
    [Fact]
    public void StakeholderItems_AreTheQuarterlyWhoWithValues()
    {
        MaturityProtocol.StakeholderItems.Select(o => o.Value)
            .Should().Equal(KeyResultProtocolSeedData.WhoWithOptions.Select(o => o.Value));
    }

    [Fact]
    public void SteeringGroupEngagement_AreTheQuarterlyEngagementLevels()
    {
        MaturityProtocol.SteeringGroupEngagement.Select(o => o.Value)
            .Should().Equal("attends_engages", "attends_limited", "contacted_no_response", "no_engagement", "do_not_know");
    }

    [Fact]
    public void FundedTimeOptions_AreTheSeededFundingTimeValues()
    {
        MaturityProtocol.FundedTimeOptions.Select(o => o.Value)
            .Should().Equal("0", "up_to_20", "21_40", "41_60", "61_80", "81_100");
    }

    // The explicit optional set — everything else is required. A question dropping out of or into this list is a
    // product decision, not a refactor.
    [Fact]
    public void OptionalQuestions_AreTheAgreedSet_AndSaySoInTheirLabel()
    {
        var optional = All.Where(q => !q.IsRequired).ToList();

        optional.Select(q => q.Code).Should().BeEquivalentTo(
        [
            "MA_OTHER_ROLES",
            "MA_NA_ENGAGEMENT_OTHER",
            "MA_NA_REGIONAL_IMPROVE",
            "MA_SG_MEMBERS_OTHER",
            "MA_STRATEGIES_OTHER",
            "MA_TRAINING_NEEDS",
            "MA_TRAINED_OTHER",
            "MA_SCREENING_OTHER",
            "MA_LIC_OTHER_WAYS",
            "MA_CAMPAIGN_URL",
        ]);
        optional.Should().OnlyContain(q => q.Label.EndsWith(MaturityProtocol.OptionalSuffix));
    }

    // BLUM-9115: the hint is the MA & QP mapping's wording verbatim. It is also what tells an LA whose assessment is
    // not online to answer "N/A", which the field accepts because it carries no URL format check.
    [Fact]
    public void PublishedAssessmentUrl_CarriesTheMappingFileHint()
    {
        var url = All.Single(q => q.Code == "MA_NA_URL");

        url.Hint.Should().Be("Please say N/A if the assessment is not published online.");
        url.QuestionType.Should().Be(MaturityFieldTypes.Text);
    }

    // BLUM-7924: one WTE per ticked role. A single question conditional on every role renders under the first ticked
    // box only, so each child must be revealed by exactly one role — and name it, or its check-your-answers row,
    // testid and CSV header collide with the other five.
    [Fact]
    public void NonFundedRoles_UseTheExclusiveNone_AndRevealOneWtePerRole()
    {
        var roles = All.Single(q => q.Code == "MA_NONFUNDED_ROLES");
        var wte = All.Where(q => q.ConditionalQuestionCode == roles.Code).ToList();

        roles.ExclusiveOptionValue.Should().Be(MaturityOptionValues.None);
        All.Should().NotContain(q => q.Code == "MA_NONFUNDED_WTE", "the single all-roles question is retired");

        wte.Select(q => q.ConditionalValue).Should().Equal(MaturityProtocol.RoleOptions.Select(o => o.Value));
        wte.Should().OnlyContain(q => q.QuestionType == MaturityFieldTypes.Radio && q.IsRequired && q.GroupLabel == null);
        wte.Select(q => q.Label).Should().OnlyHaveUniqueItems();

        foreach (var role in MaturityProtocol.RoleOptions)
        {
            var question = wte.Single(q => q.ConditionalValue == role.Value);
            question.Code.Should().Be(MaturityProtocol.NonFundedWteCode(role.Value));
            question.Label.Should().EndWith($"({role.Label})");
            question.ErrorMessage.Should().Contain(role.Label);
            question.Options.Should().BeEquivalentTo(MaturityProtocol.WteBands);
        }
    }

    // The answers the two BLUM-8438 routing rules choose between (sheet rows 44 and 102). Pinned once here, so a new
    // option fails the one test that names it rather than every routing case below.
    [Fact]
    public void SteeringGroupAndCampaignQuestions_OfferTheMappingFileAnswers()
    {
        All.Single(q => q.Code == "MA_SG_EXISTS").Options.Select(o => o.Value)
            .Should().Equal(MaturityOptionValues.Yes, MaturityOptionValues.No, MaturityOptionValues.InDevelopment);

        All.Single(q => q.Code == "MA_CAMPAIGN").Options.Select(o => o.Value).Should().Equal(
            MaturityOptionValues.Yes,
            MaturityOptionValues.No,
            MaturityOptionValues.InProgress,
            MaturityOptionValues.DontKnow,
            MaturityOptionValues.Other);
    }

    // BLUM-8438, sheet row 44: only "Yes" goes on to the steering group's members (rows 45-58). "No" and
    // "In development" both skip to the action plan (row 59).
    [Theory]
    [InlineData(MaturityOptionValues.Yes, true)]
    [InlineData(MaturityOptionValues.No, false)]
    [InlineData(MaturityOptionValues.InDevelopment, false)]
    public void SteeringGroupMembers_AreAskedOnlyWhenTheGroupExists(string answer, bool expected)
    {
        var page = MaturityProtocol.FindPage("strategic_approach", "steering_group_members")!;
        var answers = new Dictionary<string, string?> { ["MA_SG_EXISTS"] = answer };
        var visibility = MaturityAnswerValidator.Visibility(All, answers, transitive: false);

        MaturityAnswerValidator.IsPageVisible(visibility, page.VisibleWhen).Should().Be(expected);
        All.Where(q => q.Section == "strategic_approach" && q.Page == page.Code).Should().NotBeEmpty()
            .And.OnlyContain(q => visibility.IsVisible(q.Code) == expected);
    }

    // BLUM-8438, sheet row 102: "Yes", "In progress" and "Other" go on to the campaign follow-ups (rows 103-105);
    // "No" and "Don't know" skip them.
    [Theory]
    [InlineData(MaturityOptionValues.Yes, true)]
    [InlineData(MaturityOptionValues.InProgress, true)]
    [InlineData(MaturityOptionValues.Other, true)]
    [InlineData(MaturityOptionValues.No, false)]
    [InlineData(MaturityOptionValues.DontKnow, false)]
    public void CampaignFollowUps_AreAskedForYesInProgressAndOther(string answer, bool expected)
    {
        var answers = new Dictionary<string, string?> { ["MA_CAMPAIGN"] = answer };
        var visibility = MaturityAnswerValidator.Visibility(All, answers, transitive: false);

        foreach (var code in new[] { "MA_CAMPAIGN_AUDIENCE", "MA_CAMPAIGN_AIMS", "MA_CAMPAIGN_URL" })
        {
            visibility.IsVisible(code).Should().Be(expected, "{0} follows {1}", code, answer);
        }
    }

    [Fact]
    public void WideColumns_HaveUniqueHeaders_AndOneColumnPerNonRolesQuestion()
    {
        var columns = MaturityAssessmentCsv.WideColumns(maxRoles: 2);

        columns.Select(c => c.Header).Should().OnlyHaveUniqueItems();
        columns.Where(c => c.RoleIndex is null).Should().HaveCount(All.Count(q => !q.IsRoles));
        columns.Where(c => c.RoleIndex is not null).Select(c => c.Header)
            .Should().Equal("Role 1: role", "Role 1: time", "Role 2: role", "Role 2: time");
    }

    [Fact]
    public void IdFor_IsDeterministicAndDistinctPerCode()
    {
        MaturityQuestionResolver.IdFor("MA_DESIGNATED_LEAD").Should().Be(MaturityQuestionResolver.IdFor("MA_DESIGNATED_LEAD"));
        All.Select(q => q.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void ChoiceErrorMessages_SayWhatToSelect()
    {
        All.Single(q => q.Code == "MA_DESIGNATED_LEAD").ErrorMessage.Should().StartWith("Select an answer for");
        All.Single(q => q.Code == "MA_PRIORITIES").ErrorMessage.Should().StartWith("Enter an answer for");
    }
}