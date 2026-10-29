using Application.MaturityAssessments.Prefill;
using Application.MaturityAssessments.Protocol;
using Domain.Activities;
using Domain.Categories;
using Domain.MaturityAssessments;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Unit.MaturityAssessments.Prefill;

// The mapping file's column-Q worked examples, one rule at a time (BLUM-7856 AC1/AC2). Every case builds the
// organisation's activity facts and latest SUBMITTED report answers directly, so the rules are pinned without a
// database; the end-to-end path (a real submitted report becoming a prefilled answer) is an integration test.
public class QuarterlyPrefillRulesTests
{
    private static readonly DateOnly PeriodEnd = new(2027, 3, 31);

    private static readonly Dictionary<string, string> NoRecordAnswers = new(StringComparer.Ordinal);

    [Fact]
    public void StrategicNetwork_IgnoresAnEndedNetwork_AndSaysYesForACurrentOne()
    {
        var ended = Activity(QuarterlyPrefillRules.TaskNames.RegionalNetwork, ActivityStatus.Closed);
        var current = Activity(QuarterlyPrefillRules.TaskNames.RegionalNetwork, ActivityStatus.Active);
        var input = Input(
            [ended, current],
            Report(ended, ("RNW_MEMBERSHIP", "no")),
            Report(current, ("RNW_MEMBERSHIP", "yes")));

        var output = Build(input, QuarterlyPrefillRules.StrategicNetwork);

        output.Should().Contain("MA_STRATEGIC_NETWORK", "yes");
    }

    [Fact]
    public void StrategicNetwork_WritesNothing_WhenNoCurrentNetworkReported()
    {
        var ended = Activity(QuarterlyPrefillRules.TaskNames.RegionalNetwork, ActivityStatus.Closed);
        var input = Input([ended], Report(ended, ("RNW_MEMBERSHIP", "yes")));

        Build(input, QuarterlyPrefillRules.StrategicNetwork).Should().BeEmpty();
    }

    [Fact]
    public void NeedsAssessment_PublishedBeatsANewerDraft_AndCarriesItsUrlCoverageAndContributors()
    {
        var published = Activity(QuarterlyPrefillRules.TaskNames.LocalNeedsAssessment, ActivityStatus.Finished, createdAt: new DateTime(2025, 6, 1));
        var draft = Activity(QuarterlyPrefillRules.TaskNames.LocalNeedsAssessment, ActivityStatus.Active, keyResults: ["lna_published"], createdAt: new DateTime(2026, 6, 1));
        var input = Input(
            [published, draft],
            Report(published, new DateOnly(2025, 9, 30),
                ("LNA_PUBLISHED", "yes"),
                ("LNA_PUBLISHED_URL", "https://example.gov.uk/hna-2025"),
                ("LNA_INFORMATION", "prevalence,service_map,lived_experience"),
                ("LNA_CONTRIBUTORS", "police,housing,other"),
                ("LNA_CONTRIBUTORS_OTHER", "Faith groups")),
            Report(draft, new DateOnly(2026, 12, 31), ("LNA_CONTRIBUTORS", "vcse")));

        var output = Build(input, QuarterlyPrefillRules.NeedsAssessment);

        output.Should().Contain("MA_NA_PUBLISHED", "yes");
        output.Should().Contain("MA_NA_URL", "https://example.gov.uk/hna-2025");
        output.Should().Contain("MA_NA_COVERAGE", "prevalence,service_map");
        output.Should().Contain("MA_NA_ENGAGEMENT__POLICE", "yes");
        output.Should().Contain("MA_NA_ENGAGEMENT__HOUSING", "yes");
        output.Should().Contain("MA_NA_ENGAGEMENT__VCSE", "no", "the draft's contributors do not count once one is published");
        output.Should().Contain("MA_NA_ENGAGEMENT_OTHER", "Faith groups");
        output.Should().Contain("MA_NA_ENGAGEMENT__OTHER", "yes", "the Other row is answered with its free text (BLUM-9247)");
    }

    [Fact]
    public void NeedsAssessment_InProgress_UnionsTheActiveAssessmentsContributors()
    {
        var first = Activity(QuarterlyPrefillRules.TaskNames.LocalNeedsAssessment, ActivityStatus.Active, keyResults: ["lna_published"]);
        var second = Activity(QuarterlyPrefillRules.TaskNames.LocalNeedsAssessment, ActivityStatus.Active, keyResults: ["lna_contributors"]);
        var input = Input(
            [first, second],
            Report(first, ("LNA_CONTRIBUTORS", "police,housing")),
            Report(second, ("LNA_CONTRIBUTORS", "housing,vcse")));

        var output = Build(input, QuarterlyPrefillRules.NeedsAssessment);

        output.Should().Contain("MA_NA_PUBLISHED", "in_progress");
        output.Should().Contain("MA_NA_ENGAGEMENT__POLICE", "yes");
        output.Should().Contain("MA_NA_ENGAGEMENT__HOUSING", "yes");
        output.Should().Contain("MA_NA_ENGAGEMENT__VCSE", "yes");
        output.Should().Contain("MA_NA_ENGAGEMENT__LICENSING", "no");
        output.Should().NotContainKey("MA_NA_ENGAGEMENT__OTHER", "an unticked Other is never answered no");
        output.Should().NotContainKey("MA_NA_ENGAGEMENT_OTHER");
        output.Should().NotContainKey("MA_NA_URL");
    }

    // BLUM-9247: a "no" for an unticked Other would beat a prior assessment's "yes" while its free text still came
    // through from that assessment, leaving "No" beside a named stakeholder. Leaving the row unanswered keeps the pair
    // from one source.
    [Fact]
    public void NeedsAssessment_LeavesAnUntickedOther_ToThePriorAssessmentsPair()
    {
        var published = Activity(QuarterlyPrefillRules.TaskNames.LocalNeedsAssessment, ActivityStatus.Finished);
        var input = Input([published], Report(published, ("LNA_PUBLISHED", "yes"), ("LNA_CONTRIBUTORS", "police")));
        var prior = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["MA_NA_ENGAGEMENT__OTHER"] = "yes",
            ["MA_NA_ENGAGEMENT_OTHER"] = "Faith groups",
        };

        var merged = PrefillMerger.Merge(
            ["MA_NA_ENGAGEMENT__POLICE", "MA_NA_ENGAGEMENT__OTHER", "MA_NA_ENGAGEMENT_OTHER"],
            Build(input, QuarterlyPrefillRules.NeedsAssessment),
            prior,
            new Dictionary<string, string>(StringComparer.Ordinal));

        merged.Should().Equal(
            new MaturityPrefilledAnswer("MA_NA_ENGAGEMENT__POLICE", "yes", AnswerSource.Quarterly),
            new MaturityPrefilledAnswer("MA_NA_ENGAGEMENT__OTHER", "yes", AnswerSource.PriorAssessment),
            new MaturityPrefilledAnswer("MA_NA_ENGAGEMENT_OTHER", "Faith groups", AnswerSource.PriorAssessment));
    }

    [Fact]
    public void NeedsAssessment_NotPublished_WhenEveryClosedOneSaysNo()
    {
        var closed = Activity(QuarterlyPrefillRules.TaskNames.LocalNeedsAssessment, ActivityStatus.Closed);
        var input = Input([closed], Report(closed, ("LNA_PUBLISHED", "no")));

        var output = Build(input, QuarterlyPrefillRules.NeedsAssessment);

        output.Should().Contain("MA_NA_PUBLISHED", "no");
        output.Keys.Should().NotContain(k => k.StartsWith("MA_NA_ENGAGEMENT__"));
    }

    [Fact]
    public void SteeringGroup_KeepsAttendingMembersOnly_AndSaysInDevelopmentWhileActive()
    {
        var group = Activity(QuarterlyPrefillRules.TaskNames.SteeringGroup, ActivityStatus.Active);
        var input = Input(
            [group],
            Report(group,
                ("LSG_MEMBERS", "police,housing,vcse,other"),
                ("LSG_ENGAGEMENT_POLICE", "attends_engages"),
                ("LSG_ENGAGEMENT_HOUSING", "attends_limited"),
                ("LSG_ENGAGEMENT_VCSE", "contacted_no_response"),
                ("LSG_ENGAGEMENT_OTHER", "attends_engages"),
                ("LSG_MEMBERS_OTHER", "Trading standards")));

        var output = Build(input, QuarterlyPrefillRules.SteeringGroup);

        output.Should().Contain("MA_SG_EXISTS", "in_development");
        output.Should().Contain("MA_SG_MEMBERS__POLICE", "attends_engages");
        output.Should().Contain("MA_SG_MEMBERS__HOUSING", "attends_limited");
        output.Should().NotContainKey("MA_SG_MEMBERS__VCSE");
        output.Should().Contain("MA_SG_MEMBERS__OTHER", "attends_engages", "the Other row is answered with its free text (BLUM-9247)");
        output.Should().Contain("MA_SG_MEMBERS_OTHER", "Trading standards");
    }

    // BLUM-9247: the free text never shows without its radio, so an Other the mapping rule does not carry (it was
    // contacted, or does not engage) takes its text with it — while an attending Other on another report still counts.
    [Fact]
    public void SteeringGroup_CarriesOtherTextOnlyFromReportsWhereOtherAttends()
    {
        var lapsed = Activity(QuarterlyPrefillRules.TaskNames.SteeringGroup, ActivityStatus.Active);
        var current = Activity(QuarterlyPrefillRules.TaskNames.SteeringGroup, ActivityStatus.Active);
        var lapsedReport = Report(lapsed,
            ("LSG_MEMBERS", "other"),
            ("LSG_ENGAGEMENT_OTHER", "contacted_no_response"),
            ("LSG_MEMBERS_OTHER", "Trading standards"));

        var alone = Build(Input([lapsed], lapsedReport), QuarterlyPrefillRules.SteeringGroup);

        alone.Should().NotContainKey("MA_SG_MEMBERS__OTHER");
        alone.Should().NotContainKey("MA_SG_MEMBERS_OTHER");

        var withAttending = Build(
            Input(
                [lapsed, current],
                lapsedReport,
                Report(current, ("LSG_MEMBERS", "other"), ("LSG_ENGAGEMENT_OTHER", "attends_limited"), ("LSG_MEMBERS_OTHER", "Fire service"))),
            QuarterlyPrefillRules.SteeringGroup);

        withAttending.Should().Contain("MA_SG_MEMBERS__OTHER", "attends_limited");
        withAttending.Should().Contain("MA_SG_MEMBERS_OTHER", "Fire service");
    }

    [Fact]
    public void SteeringGroup_AFinishedGroupIsAYes_AndAClosedOneIsNotEvidence()
    {
        var finished = Activity(QuarterlyPrefillRules.TaskNames.SteeringGroup, ActivityStatus.Finished);
        var closed = Activity(QuarterlyPrefillRules.TaskNames.SteeringGroup, ActivityStatus.Closed);

        Build(Input([finished]), QuarterlyPrefillRules.SteeringGroup).Should().Contain("MA_SG_EXISTS", "yes");
        Build(Input([closed]), QuarterlyPrefillRules.SteeringGroup).Should().BeEmpty();
    }

    [Fact]
    public void StrategyAndActionPlan_AnyYesAcrossStatuses_AndStrategiesFromClosedOnly()
    {
        var adopted = Activity(QuarterlyPrefillRules.TaskNames.StrategyAndActionPlan, ActivityStatus.Finished);
        var refresh = Activity(QuarterlyPrefillRules.TaskNames.StrategyAndActionPlan, ActivityStatus.Active);
        var input = Input(
            [adopted, refresh],
            Report(adopted, ("SAP_PLAN_DEVELOPED", "yes"), ("SAP_OTHER_STRATEGIES", "alcohol,suicide"), ("SAP_HWB_PRESENTED", "no")),
            Report(refresh, ("SAP_PLAN_DEVELOPED", "no"), ("SAP_OTHER_STRATEGIES", "drugs")));

        var output = Build(input, QuarterlyPrefillRules.StrategyAndActionPlan);

        output.Should().Contain("MA_ACTION_PLAN", "yes");
        output.Should().Contain("MA_STRATEGIES__ALCOHOL", "yes");
        output.Should().Contain("MA_STRATEGIES__SUICIDE", "yes");
        output.Should().Contain("MA_STRATEGIES__DRUGS", "no", "an Active activity's strategies are not yet embedded");
        output.Should().Contain("MA_STRATEGIES__MENTAL_HEALTH", "no");
        output.Should().Contain("MA_HWB_PRESENTED", "no");
    }

    [Fact]
    public void Training_UnionsClosedActivities_AndLeavesInProgressToTheLa()
    {
        var licensing = Activity(QuarterlyPrefillRules.TaskNames.Training, ActivityStatus.Closed);
        var housing = Activity(QuarterlyPrefillRules.TaskNames.Training, ActivityStatus.Active);
        var input = Input(
            [licensing, housing],
            Report(licensing, ("TRN_SERVICE_AREAS", "licensing,other"), ("TRN_SERVICE_AREAS_OTHER", "Leisure centre staff")),
            Report(housing, ("TRN_SERVICE_AREAS", "housing")));

        var output = Build(input, QuarterlyPrefillRules.Training);

        output.Should().Contain("MA_TRAINED_WORKFORCES__LICENSING", "yes");
        output.Should().Contain("MA_TRAINED_WORKFORCES__HOUSING", "no", "the Active activity has not finished training anyone");
        output.Should().Contain("MA_TRAINED_WORKFORCES__OTHER", "yes", "the Other row is answered with its free text (BLUM-9247)");
        output.Should().Contain("MA_TRAINED_OTHER", "Leisure centre staff");
    }

    [Fact]
    public void Screening_NamesAServiceOnce_AcrossSeveralActivities()
    {
        var first = Activity(QuarterlyPrefillRules.TaskNames.ScreeningTools, ActivityStatus.Closed);
        var second = Activity(QuarterlyPrefillRules.TaskNames.ScreeningTools, ActivityStatus.Finished);
        var input = Input(
            [first, second],
            Report(first, ("SCR_SERVICES", "housing,debt")),
            Report(second, ("SCR_SERVICES", "housing")));

        var output = Build(input, QuarterlyPrefillRules.Screening);

        output.Should().Contain("MA_SCREENING__HOUSING", "yes");
        output.Should().Contain("MA_SCREENING__DEBT", "yes");
        output.Should().Contain("MA_SCREENING__POLICE", "no");
        output.Should().NotContainKey("MA_SCREENING__OTHER", "an unticked Other is never answered no");
        output.Should().NotContainKey("MA_SCREENING_OTHER");
    }

    // A report holding Other's free text without Other ticked cannot come from a page save, which drops hidden
    // answers; if one exists anyway, its text must not arrive without the Other row.
    [Fact]
    public void Training_IgnoresOtherText_WhenTheReportDidNotTickOther()
    {
        var training = Activity(QuarterlyPrefillRules.TaskNames.Training, ActivityStatus.Finished);
        var input = Input([training], Report(training, ("TRN_SERVICE_AREAS", "licensing"), ("TRN_SERVICE_AREAS_OTHER", "Leisure centre staff")));

        var output = Build(input, QuarterlyPrefillRules.Training);

        output.Should().Contain("MA_TRAINED_WORKFORCES__LICENSING", "yes");
        output.Should().NotContainKey("MA_TRAINED_WORKFORCES__OTHER");
        output.Should().NotContainKey("MA_TRAINED_OTHER");
    }

    [Fact]
    public void Screening_AnswersTheOtherRow_WithItsFreeText()
    {
        var screening = Activity(QuarterlyPrefillRules.TaskNames.ScreeningTools, ActivityStatus.Finished);
        var input = Input([screening], Report(screening, ("SCR_SERVICES", "debt,other"), ("SCR_SERVICES_OTHER", "Fire service")));

        var output = Build(input, QuarterlyPrefillRules.Screening);

        output.Should().Contain("MA_SCREENING__OTHER", "yes");
        output.Should().Contain("MA_SCREENING_OTHER", "Fire service");
    }

    [Fact]
    public void LicenceApplications_InvitedIsAllYes_RefusedIsAnyYes()
    {
        var activities = Enumerable.Range(0, 5)
            .Select(_ => Activity(QuarterlyPrefillRules.TaskNames.LicenceApplications, ActivityStatus.Active))
            .ToList();
        var reports = activities
            .Select((a, i) => Report(a, ("LRA_INVITED", i < 4 ? "yes" : "no"), ("LRA_REFUSED", i == 0 ? "yes" : "no")))
            .ToArray();

        var output = Build(Input(activities, reports), QuarterlyPrefillRules.LicenceApplications);

        output.Should().Contain("MA_LIC_INVITED", "no");
        output.Should().Contain("MA_LIC_RESTRICTED", "yes");
    }

    [Fact]
    public void Campaign_DeliveredWithinTwelveMonths_IsAYesWithItsAudience()
    {
        var campaign = Activity(
            QuarterlyPrefillRules.TaskNames.Campaigns,
            ActivityStatus.Finished,
            recordAnswers: new Dictionary<string, string> { ["EDU_WHO_FOR"] = "young_people,other", ["EDU_WHO_FOR_OTHER"] = "Shift workers" });
        var input = Input([campaign], Report(campaign, PeriodEnd.AddMonths(-8), ("CAM_LIVE", "yes")));

        var output = Build(input, QuarterlyPrefillRules.Campaign);

        output.Should().Contain("MA_CAMPAIGN", "yes");
        output.Should().Contain("MA_CAMPAIGN_AUDIENCE", "Young people, Shift workers");
    }

    [Fact]
    public void Campaign_OlderThanTwelveMonthsIsIgnored_AndAnActiveOneIsInProgress()
    {
        var old = Activity(QuarterlyPrefillRules.TaskNames.Campaigns, ActivityStatus.Finished);
        var running = Activity(QuarterlyPrefillRules.TaskNames.Campaigns, ActivityStatus.Active, keyResults: ["cam_live"]);
        var input = Input([old, running], Report(old, PeriodEnd.AddMonths(-14), ("CAM_LIVE", "yes")));

        var output = Build(input, QuarterlyPrefillRules.Campaign);

        output.Should().Contain("MA_CAMPAIGN", "in_progress");
    }

    [Fact]
    public void Advertising_SomeSpacesRestrictedIsAYes()
    {
        var some = Activity(QuarterlyPrefillRules.TaskNames.Advertising, ActivityStatus.Active);
        var none = Activity(QuarterlyPrefillRules.TaskNames.Advertising, ActivityStatus.Active);
        var input = Input(
            [some, none],
            Report(some, ("ADV_RESTRICTIONS", "yes_some"), ("ADV_OTHER_RESTRICTIONS", "no")),
            Report(none, ("ADV_RESTRICTIONS", "no"), ("ADV_OTHER_RESTRICTIONS", "yes")));

        var output = Build(input, QuarterlyPrefillRules.Advertising);

        output.Should().Contain("MA_ADV_RESTRICTED", "yes");
        output.Should().Contain("MA_ADV_OTHER_COMMODITIES", "yes");
    }

    [Fact]
    public void FundedRoles_OneRepeaterRowPerCurrentRoleActivity_DuplicatesKept()
    {
        var first = Activity("Public Health Analyst", ActivityStatus.Active, category: CategoryNames.PeopleAndRoles, createdAt: new DateTime(2026, 1, 1), recordAnswers: new Dictionary<string, string> { ["PR_FUNDING_TIME"] = "21_40" });
        var second = Activity("Public Health Analyst", ActivityStatus.Active, category: CategoryNames.PeopleAndRoles, createdAt: new DateTime(2026, 2, 1), recordAnswers: new Dictionary<string, string> { ["PR_FUNDING_TIME"] = "41_60" });
        var other = Activity(QuarterlyPrefillRules.TaskNames.OtherRole, ActivityStatus.Active, category: CategoryNames.PeopleAndRoles, createdAt: new DateTime(2026, 3, 1), customTaskText: "Data analyst", recordAnswers: new Dictionary<string, string> { ["PR_FUNDING_TIME"] = "up_to_20" });
        var ended = Activity("Public Health Officer", ActivityStatus.Closed, category: CategoryNames.PeopleAndRoles, recordAnswers: new Dictionary<string, string> { ["PR_FUNDING_TIME"] = "81_100" });

        var output = Build(Input([first, second, other, ended]), QuarterlyPrefillRules.FundedRoles);

        MaturityRolesAnswer.TryParse(output["MA_FUNDED_ROLES"], out var entries).Should().BeTrue();
        entries.Should().Equal(
            new MaturityRolesAnswer.Entry("public_health_analyst", "21_40", null),
            new MaturityRolesAnswer.Entry("public_health_analyst", "41_60", null),
            new MaturityRolesAnswer.Entry("other", "up_to_20", "Data analyst"));
        output.Should().Contain("MA_OTHER_ROLES", "Data analyst");
    }

    // BLUM-9112: "None" is the LA's answer alone — a role activity whose task happens to be named "None" is an
    // unknown role, so it becomes an "Other role" row, never the None sentinel.
    [Fact]
    public void FundedRoles_NeverWriteNone()
    {
        var named = Activity("None", ActivityStatus.Active, category: CategoryNames.PeopleAndRoles, recordAnswers: new Dictionary<string, string> { ["PR_FUNDING_TIME"] = "21_40" });

        var output = Build(Input([named]), QuarterlyPrefillRules.FundedRoles);

        MaturityRolesAnswer.TryParse(output["MA_FUNDED_ROLES"], out var entries).Should().BeTrue();
        entries.Should().ContainSingle().Which.Should().Be(new MaturityRolesAnswer.Entry("other", "21_40", "None"));
    }

    [Fact]
    public void Build_MergesEveryFamily()
    {
        var network = Activity(QuarterlyPrefillRules.TaskNames.RegionalNetwork, ActivityStatus.Active);
        var referrals = Activity(QuarterlyPrefillRules.TaskNames.Referrals, ActivityStatus.Active);
        var input = Input([network, referrals], Report(network, ("RNW_MEMBERSHIP", "yes")), Report(referrals, ("REF_PATHWAYS", "yes")));

        var output = QuarterlyPrefillRules.Build(input);

        output.Should().Contain("MA_STRATEGIC_NETWORK", "yes");
        output.Should().Contain("MA_REFERRAL_PATHWAY", "yes");
    }

    // ---- fixtures ---------------------------------------------------------------------------------------

    private static Dictionary<string, string> Build(QuarterlyPrefillInput input, Action<QuarterlyPrefillInput, Dictionary<string, string>> rule)
    {
        var output = new Dictionary<string, string>(StringComparer.Ordinal);
        rule(input, output);
        return output;
    }

    private static ActivityFacts Activity(
        string taskName,
        ActivityStatus status,
        string category = "Strategy",
        IReadOnlyList<string>? keyResults = null,
        DateTime? createdAt = null,
        string? customTaskText = null,
        IReadOnlyDictionary<string, string>? recordAnswers = null) =>
        new(
            Guid.NewGuid(),
            taskName,
            category,
            status,
            customTaskText,
            createdAt ?? new DateTime(2026, 1, 1),
            (keyResults ?? []).ToHashSet(StringComparer.Ordinal),
            recordAnswers ?? NoRecordAnswers);

    private static (Guid ActivityId, LatestReport Report) Report(ActivityFacts activity, params (string Code, string Value)[] answers) =>
        Report(activity, PeriodEnd.AddMonths(-3), answers);

    private static (Guid ActivityId, LatestReport Report) Report(ActivityFacts activity, DateOnly periodEnd, params (string Code, string Value)[] answers) =>
        (activity.ActivityId, new LatestReport(Guid.NewGuid(), periodEnd, answers.ToDictionary(a => a.Code, a => a.Value, StringComparer.Ordinal)));

    private static QuarterlyPrefillInput Input(IReadOnlyList<ActivityFacts> activities, params (Guid ActivityId, LatestReport Report)[] reports) =>
        new(
            activities,
            reports.ToDictionary(r => r.ActivityId, r => r.Report),
            PeriodEnd,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["young_people"] = "Young people" });
}