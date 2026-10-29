using FluentAssertions;
using Infrastructure.Persistence.Seed.MaturityBaseline;
using Xunit;

namespace Application.Tests.Unit.Seed;

// The 2025 survey → catalogue translation (BLUM-7857 AC1), one rule per fact. A hand-written row rather than a real
// one so each expectation is about the rule, not about which council happened to answer that way.
public class MaturityBaselineMappingTests
{
    private static MaturityBaselineRow Row(params (string Column, string Value)[] cells) =>
        new(2, "1", "London", "Test Council", cells.ToDictionary(c => c.Column, c => c.Value, StringComparer.Ordinal));

    private static Dictionary<string, string> Map(params (string Column, string Value)[] cells) =>
        MaturityBaselineMapping.Map(Row(cells)).ToDictionary(a => a.Code, a => a.Value, StringComparer.Ordinal);

    [Fact]
    public void NonFundedRoles_TickRolesWithTime_AndCopyEachRolesBandToItsOwnWte()
    {
        var mapped = Map(
            ("wte_public_health_officer", ">0 - 0.2 WTE"),
            ("wte_public_health_analyst", ">0.2 -0.4 WTE"),
            ("wte_public_health_practitioner", "0 WTE"),
            ("wte_public_health_specialist", "0 WTE"),
            ("wte_public_health_intelligence_specialist", "0 WTE"),
            ("wte_consultant_public_health", ">0.8 - 1 WTE"));

        mapped["MA_NONFUNDED_ROLES"].Should().Be("public_health_officer,public_health_analyst,consultant_in_public_health");
        mapped.Where(a => a.Key.StartsWith("MA_NONFUNDED_WTE__", StringComparison.Ordinal)).Should().BeEquivalentTo(
            new Dictionary<string, string>
            {
                ["MA_NONFUNDED_WTE__PUBLIC_HEALTH_OFFICER"] = "up_to_0_2",
                ["MA_NONFUNDED_WTE__PUBLIC_HEALTH_ANALYST"] = "0_2_to_0_4",
                ["MA_NONFUNDED_WTE__CONSULTANT_IN_PUBLIC_HEALTH"] = "0_8_to_1",
            },
            "a zero-band role is unticked, so its WTE question is hidden and gets no answer");
    }

    [Fact]
    public void NonFundedRoles_AllZeroIsNone_WithNoWte()
    {
        var mapped = Map(("wte_public_health_officer", "0 WTE"), ("wte_consultant_public_health", "0 WTE"));

        mapped.Should().Contain("MA_NONFUNDED_ROLES", "none");
        mapped.Keys.Should().NotContain(k => k.StartsWith("MA_NONFUNDED_WTE", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Yes;", "yes", null)]
    [InlineData("No", "no", null)]
    [InlineData("Yes;No;", "other", "Yes;No")]
    [InlineData("No;It is being included in 26/27;", "other", "No;It is being included in 26/27")]
    public void BusinessPlan_IsYesOrNoOnlyWhenTickedAlone(string cell, string expected, string? otherText)
    {
        var mapped = Map(("business_plan_includes_gambling_harm_fy2526", cell));

        mapped["MA_BUSINESS_PLAN"].Should().Be(expected);
        if (otherText is null)
        {
            mapped.Should().NotContainKey("MA_BUSINESS_PLAN_OTHER");
        }
        else
        {
            mapped["MA_BUSINESS_PLAN_OTHER"].Should().Be(otherText);
        }
    }

    [Fact]
    public void Radios_MatchTheSurveyWordingCaseInsensitively()
    {
        var mapped = Map(
            ("designated_gambling_harm_lead", "yes"),
            ("part_of_strategic_network", "No - but we would like to be"),
            ("hna_published", "In progress"),
            ("want_regional_hna", "We already have one that we don't use"),
            ("steering_group_status", "In development"),
            ("workplace_policies_for_staff", "No - but we are planning to explore this"),
            ("treatment_support_access_process", "Partly"),
            ("policy_includes_local_area_profile", "It includes a link to our JSNA/HNA"),
            ("ad_restrictions_gambling_council_spaces", "No, but we are considering doing so"),
            ("other_prevention_services", "No but we plan to"),
            ("last_presented_to_hwb", "Prior to FY 2022/23"));

        mapped.Should().Contain("MA_DESIGNATED_LEAD", "yes");
        mapped.Should().Contain("MA_STRATEGIC_NETWORK", "would_like_to");
        mapped.Should().Contain("MA_NA_PUBLISHED", "in_progress");
        mapped.Should().Contain("MA_NA_REGIONAL", "have_dont_use");
        mapped.Should().Contain("MA_SG_EXISTS", "in_development");
        mapped.Should().Contain("MA_STAFF_SUPPORT", "planning_to_explore");
        mapped.Should().Contain("MA_REFERRAL_PATHWAY", "partly");
        mapped.Should().Contain("MA_LIC_LOCAL_PROFILE", "links_to_jsna");
        mapped.Should().Contain("MA_ADV_RESTRICTED", "considering");
        mapped.Should().Contain("MA_OTHER_SERVICES", "no_plan_to");
        mapped.Should().Contain("MA_HWB_LAST", "prior_fy_2022_23");
    }

    [Fact]
    public void RadioOrOther_KeepsFreeWordingAsOtherPlusText()
    {
        var mapped = Map(
            ("consider_pooling_resources", "Not yet discussed"),
            ("action_plan_status", "A new action plan is in progress, our previous action plan is now out of date."),
            ("harms_in_planning_guidance", "Don't know"),
            ("awareness_campaign_past_year", "Incorporated into the wider mental health signposting campaign"));

        mapped.Should().Contain("MA_SHARED_RESOURCES", "other");
        mapped.Should().Contain("MA_SHARED_RESOURCES_OTHER", "Not yet discussed");
        mapped.Should().Contain("MA_ACTION_PLAN", "other");
        mapped.Should().Contain("MA_PLAN_GUIDANCE", "dont_know");
        mapped.Should().Contain("MA_CAMPAIGN", "other");
        mapped["MA_CAMPAIGN_OTHER"].Should().StartWith("Incorporated");
    }

    [Fact]
    public void Matrices_MapEachSurveyColumnToItsCatalogueItem()
    {
        var mapped = Map(
            ("stakeholder_icb_representative", "Contacted but no response"),
            ("stakeholder_drug_treatment_services", "Don't know"),
            ("steering_la_public_health_rep", "Yes (attends and engages)"),
            ("steering_police", "Yes (attends but limited engagement)"),
            ("steering_housing", "No"),
            ("strategy_suicide", "Do not have a strategy for that topic area"),
            ("strategy_drugs", "In progress"),
            ("training_commissioned_teachers_schools", "In progress"),
            ("screening_tool_primary_care", "Yes (and report the data e.g. number offered/uptake)"),
            ("screening_tool_debt_advice", "Yes (but no reporting)"));

        mapped.Should().Contain("MA_NA_ENGAGEMENT__ICB", "contacted_no_response");
        mapped.Should().Contain("MA_NA_ENGAGEMENT__DRUG", "dont_know");
        mapped.Should().Contain("MA_SG_MEMBERS__LA_PUBLIC_HEALTH", "attends_engages");
        mapped.Should().Contain("MA_SG_MEMBERS__POLICE", "attends_limited");
        mapped.Should().Contain("MA_SG_MEMBERS__HOUSING", "no_engagement");
        mapped.Should().Contain("MA_STRATEGIES__SUICIDE", "no_strategy");
        mapped.Should().Contain("MA_STRATEGIES__DRUGS", "in_development");
        mapped.Should().Contain("MA_TRAINED_WORKFORCES__TEACHERS_SCHOOLS", "in_progress");
        mapped.Should().Contain("MA_SCREENING__PRIMARY_CARE", "yes");
        mapped.Should().Contain("MA_SCREENING__DEBT", "yes");
    }

    [Fact]
    public void Coverage_TicksTheYesColumns()
    {
        var mapped = Map(
            ("hna_data_prevalence", "Yes"),
            ("hna_data_specific_groups", "No"),
            ("hna_stakeholder_engagement", "Yes"),
            ("hna_recommendations", "NA"));

        mapped["MA_NA_COVERAGE"].Should().Be("prevalence,stakeholder_engagement");
    }

    [Theory]
    [InlineData("hna_latest_published", "MA_NA_LAST_PUBLISHED", "2025.0", "2025")]
    [InlineData("hna_latest_published", "MA_NA_LAST_PUBLISHED", "Pre 2020", "pre_2020")]
    [InlineData("hna_latest_published", "MA_NA_LAST_PUBLISHED", "2018", "pre_2020")]
    [InlineData("hna_next_due", "MA_NA_NEXT_DUE", "No date set", "no_date_set")]
    [InlineData("hna_next_due", "MA_NA_NEXT_DUE", "2029.0", "2029")]
    [InlineData("hna_next_due", "MA_NA_NEXT_DUE", "2035", "post_2030")]
    public void Years_AreBandedIntoTheCatalogueLists(string column, string code, string cell, string expected)
    {
        Map((column, cell)).Should().Contain(code, expected);
    }

    [Fact]
    public void Years_OutsideTheListAreDropped()
    {
        Map(("hna_latest_published", "2030")).Should().NotContainKey("MA_NA_LAST_PUBLISHED");
        Map(("hna_next_due", "2024")).Should().NotContainKey("MA_NA_NEXT_DUE");
    }

    [Fact]
    public void CampaignAims_SplitsTheList_AndAllOfTheAboveTicksEverything()
    {
        Map(("campaign_aims", "Promoting help/support;Reducing stigma;"))["MA_CAMPAIGN_AIMS"]
            .Should().Be("promoting_help,reducing_stigma");

        Map(("campaign_aims", "All of the above"))["MA_CAMPAIGN_AIMS"].Split(',')
            .Should().BeEquivalentTo(["promoting_help", "reducing_stigma", "awareness_raising"]);

        var withUnknown = Map(("campaign_aims", "Reducing stigma;Share where information can be found;"));
        withUnknown["MA_CAMPAIGN_AIMS"].Should().Be("reducing_stigma,other");
        withUnknown["MA_CAMPAIGN_AIMS_OTHER"].Should().Be("Share where information can be found");
    }

    [Fact]
    public void VcseNames_MatchTheCatalogue_AndKeepLocalOnesAsOther()
    {
        var mapped = Map(
            ("commissioning_vcse_for_prevention", "Yes"),
            ("commissioned_vcse_names", "Chapter One, part of the charity Gambling with Lives;GamCare;"),
            ("commissioned_vcse_services", "Training for frontline workers;Lived experience research;"),
            ("engaged_vcse_names", "Citizens Advice;light project Peterborough;BetKnowMore;"));

        mapped.Should().Contain("MA_VCSE_COMMISSIONING", "yes");
        mapped.Should().Contain("MA_VCSE_COMMISSIONED", "chapter_one,gamcare");
        mapped.Should().NotContainKey("MA_VCSE_COMMISSIONED_OTHER");
        mapped.Should().Contain("MA_VCSE_SERVICES", "training_frontline,other");
        mapped.Should().Contain("MA_VCSE_SERVICES_OTHER", "Lived experience research");
        mapped.Should().Contain("MA_VCSE_ENGAGED_NAMES", "citizens_advice,betknowmore_uk,other");
        mapped.Should().Contain("MA_VCSE_ENGAGED_NAMES_OTHER", "light project Peterborough");
    }

    [Fact]
    public void FreeText_SkipsTheSurveysNotAnsweredSpellings_AndKeepsLongTextVerbatim()
    {
        var longText = new string('p', 900);
        var mapped = Map(
            ("strategic_network_details", "NA"),
            ("hna_link", "N/A"),
            ("training_needed", "-"),
            ("priorities_preventing_gambling_harm", longText),
            ("other_roles_wte", "  Service Manager  "));

        mapped.Should().NotContainKeys("MA_STRATEGIC_NETWORK_DETAILS", "MA_NA_URL", "MA_TRAINING_NEEDS");
        mapped["MA_PRIORITIES"].Should().Be(longText);
        mapped["MA_OTHER_ROLES"].Should().Be("Service Manager");
    }

    [Fact]
    public void Conform_DropsAnythingTheCatalogueCannotHold()
    {
        var raw = new List<MaturityBaselineMappedAnswer>
        {
            new("MA_DESIGNATED_LEAD", "maybe"),
            new("MA_NA_COVERAGE", "prevalence,unknown"),
            new("MA_NOT_A_QUESTION", "yes"),
            new("MA_PRIORITIES", " "),
            new("MA_STAFF_SUPPORT", "yes"),
        };

        MaturityBaselineMapping.Conform(raw).Should().BeEquivalentTo(
        [
            new MaturityBaselineMappedAnswer("MA_NA_COVERAGE", "prevalence"),
            new MaturityBaselineMappedAnswer("MA_STAFF_SUPPORT", "yes"),
        ]);
    }
}