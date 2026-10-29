using System.Globalization;
using System.Text.RegularExpressions;
using Application.MaturityAssessments.Protocol;

namespace Infrastructure.Persistence.Seed.MaturityBaseline;

public sealed record MaturityBaselineMappedAnswer(string Code, string Value);

// Translates one 2025 survey row into maturity-assessment answers (BLUM-7857 AC1): survey column → MA_* code, survey
// wording → catalogue option VALUE. Pure and total: a value the catalogue cannot represent is dropped (or, where the
// question has an "Other" option, kept as other + its free-text companion), never guessed. Free text is kept
// verbatim even past the 500-character ceiling — the LA sees the overrun as a character-count error on Continue,
// which beats silently truncating what they told DHSC last year.
//
// The column names are the Microsoft Forms export headers; the item and option values are the catalogue's, so a
// mapping slip surfaces in MaturityBaselineCsvTests (every mapped value must survive Conform unchanged).
public static partial class MaturityBaselineMapping
{
    private const string Yes = MaturityOptionValues.Yes;
    private const string No = MaturityOptionValues.No;
    private const string Other = MaturityOptionValues.Other;
    private const string NoWte = "0";

    private static readonly HashSet<string> NotAnswered = new(StringComparer.OrdinalIgnoreCase) { "na", "n/a", "-", "n.a", "n.a." };

    private static readonly IReadOnlyDictionary<string, string> YesNo = Table((Yes, "Yes"), (No, "No"));

    private static readonly IReadOnlyDictionary<string, string> YesNoDontKnow = Table(
        (Yes, "Yes"),
        (No, "No"),
        (MaturityOptionValues.DontKnow, "Don't know"));

    private static readonly IReadOnlyDictionary<string, string> YesNoInProgress = Table(
        (Yes, "Yes"),
        (No, "No"),
        (MaturityOptionValues.InProgress, "In progress"));

    private static readonly IReadOnlyDictionary<string, string> StrategicNetwork = Table(
        (Yes, "Yes"),
        (No, "No"),
        (MaturityOptionValues.WouldLikeTo, "No - but we would like to be"),
        (MaturityOptionValues.NotSure, "Don't know"),
        (MaturityOptionValues.NotSure, "Not sure"));

    private static readonly IReadOnlyDictionary<string, string> SharedResources = Table(
        (Yes, "Yes (this is something we would like to do)"),
        (Yes, "Yes"),
        (No, "No"),
        (MaturityOptionValues.Considering, "Considering this but undecided"),
        (MaturityOptionValues.DontKnow, "Don't know"));

    private static readonly IReadOnlyDictionary<string, string> Engagement = Table(
        (Yes, "Yes"),
        (No, "No"),
        (MaturityOptionValues.ContactedNoResponse, "Contacted but no response"),
        (MaturityOptionValues.DontKnow, "Don't know"));

    private static readonly IReadOnlyDictionary<string, string> RegionalHna = Table(
        (Yes, "Yes"),
        (No, "No"),
        (MaturityOptionValues.Maybe, "Maybe"),
        (MaturityOptionValues.HaveAndUse, "We already have one that we use"),
        (MaturityOptionValues.HaveDontUse, "We already have one that we don't use"),
        (MaturityOptionValues.NeedsUpdating, "Our regional HNA needs updating"),
        (MaturityOptionValues.DontKnow, "Don't know"));

    private static readonly IReadOnlyDictionary<string, string> SteeringGroup = Table(
        (Yes, "Yes"),
        (No, "No"),
        (MaturityOptionValues.InDevelopment, "In development"));

    private static readonly IReadOnlyDictionary<string, string> SteeringMember = Table(
        (MaturityOptionValues.AttendsEngages, "Yes (attends and engages)"),
        (MaturityOptionValues.AttendsLimited, "Yes (attends but limited engagement)"),
        (MaturityOptionValues.ContactedNoResponse, "Contacted but no response"),
        (MaturityOptionValues.NoEngagement, "No"),
        (MaturityOptionValues.DoNotKnow, "Don't know"));

    private static readonly IReadOnlyDictionary<string, string> Strategy = Table(
        (Yes, "Yes"),
        (No, "No"),
        (MaturityOptionValues.NoStrategy, "Do not have a strategy for that topic area"),
        (MaturityOptionValues.InDevelopment, "In progress"),
        (MaturityOptionValues.InDevelopment, "In development"));

    private static readonly IReadOnlyDictionary<string, string> HwbYears = Table(
        ("fy_2025_26", "FY 2025/26"),
        ("fy_2024_25", "FY 2024/25"),
        ("fy_2023_24", "FY 2023/24"),
        ("fy_2022_23", "FY 2022/23"),
        ("prior_fy_2022_23", "Prior to FY 2022/23"));

    private static readonly IReadOnlyDictionary<string, string> StaffSupport = Table(
        (Yes, "Yes"),
        (No, "No"),
        (MaturityOptionValues.PlanningToExplore, "No - but we are planning to explore this"),
        (MaturityOptionValues.DontKnow, "Don't know"));

    private static readonly IReadOnlyDictionary<string, string> Trained = Table(
        (Yes, "Yes"),
        (No, "No"),
        (MaturityOptionValues.InProgress, "In progress"),
        (MaturityOptionValues.DontKnow, "Don't know"));

    private static readonly IReadOnlyDictionary<string, string> Screening = Table(
        (Yes, "Yes (but no reporting)"),
        (Yes, "Yes (and report the data e.g. number offered/uptake)"),
        (Yes, "Yes"),
        (No, "No"),
        (MaturityOptionValues.DontKnow, "Don't know"));

    private static readonly IReadOnlyDictionary<string, string> Referral = Table(
        (Yes, "Yes"),
        (No, "No"),
        (MaturityOptionValues.Partly, "Partly"),
        (MaturityOptionValues.DontKnow, "Don't know"));

    private static readonly IReadOnlyDictionary<string, string> LocalProfile = Table(
        (Yes, "Yes"),
        (No, "No"),
        (MaturityOptionValues.DontKnow, "Don't know"),
        (MaturityOptionValues.LinksToJsna, "It includes a link to our JSNA/HNA"));

    private static readonly IReadOnlyDictionary<string, string> Campaign = Table(
        (Yes, "Yes"),
        (No, "No"),
        (MaturityOptionValues.InProgress, "In progress"),
        (MaturityOptionValues.DontKnow, "Don't know"));

    private static readonly IReadOnlyDictionary<string, string> AdvertisingRestricted = Table(
        (Yes, "Yes"),
        (No, "No"),
        (MaturityOptionValues.InProgress, "In progress"),
        (MaturityOptionValues.Considering, "No, but we are considering doing so"),
        (MaturityOptionValues.DontKnow, "Don't know"));

    private static readonly IReadOnlyDictionary<string, string> AdvertisingType = Table(
        ("all_gambling", "All types of gambling"),
        ("all_excluding_lottery", "All gambling excluding lottery or lottery funded projects"),
        ("online_gambling", "Online gambling"),
        (MaturityOptionValues.DontKnow, "Don't know"));

    private static readonly IReadOnlyDictionary<string, string> OtherCommodities = Table(
        (Yes, "Yes"),
        (No, "No"),
        (MaturityOptionValues.InProgress, "In progress"),
        (MaturityOptionValues.Considering, "No, but we are considering doing so"));

    private static readonly IReadOnlyDictionary<string, string> OtherServices = Table(
        (Yes, "Yes"),
        (No, "No"),
        (MaturityOptionValues.NoButPlanTo, "No but we plan to"),
        (MaturityOptionValues.DontKnow, "Don't know"));

    private static readonly IReadOnlyDictionary<string, string> CampaignAims = LabelTable(
        MaturityProtocol.CampaignAims);

    private static readonly IReadOnlyDictionary<string, string> VcseServices = LabelTable(
        MaturityProtocol.VcseServices);

    private static readonly IReadOnlyDictionary<string, string> VcseOrganisations = LabelTable(
        MaturityProtocol.VcseOrganisations,
        ("betknowmore_uk", "BetKnowMore"),
        ("betknowmore_uk", "Betknowmore"),
        ("chapter_one", "Chapter One"),
        ("cega", "The Coalition to End Gambling Ads"),
        ("cega", "Coalition to End Gambling Ads"),
        ("cega", "CEGA"),
        ("rcgp", "RCGP"),
        ("gambling_lived_experience_network", "Gambling Lived Experience Network"));

    // Survey band (spaces stripped, lower-cased) → the catalogue's WteBands value. The survey stopped at 1 WTE per
    // role, so over_1 is never produced here.
    private static readonly IReadOnlyDictionary<string, string> WteBandValues = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["0wte"] = NoWte,
        [">0-0.2wte"] = "up_to_0_2",
        [">0.2-0.4wte"] = "0_2_to_0_4",
        [">0.4-0.6wte"] = "0_4_to_0_6",
        [">0.6-0.8wte"] = "0_6_to_0_8",
        [">0.8-1wte"] = "0_8_to_1",
    };

    private static readonly (string Column, string Role)[] WteColumns =
    [
        ("wte_public_health_officer", "public_health_officer"),
        ("wte_public_health_analyst", "public_health_analyst"),
        ("wte_public_health_practitioner", "public_health_practitioner"),
        ("wte_public_health_specialist", "public_health_specialist"),
        ("wte_public_health_intelligence_specialist", "public_health_intelligence_specialist"),
        ("wte_consultant_public_health", "consultant_in_public_health"),
    ];

    private static readonly (string Column, string Value)[] CoverageColumns =
    [
        ("hna_data_prevalence", "prevalence"),
        ("hna_data_specific_groups", "specific_groups"),
        ("hna_location_premises", "venue_locations"),
        ("hna_mapping_service_provision", "service_map"),
        ("hna_future_opportunities_challenges", "future"),
        ("hna_stakeholder_engagement", "stakeholder_engagement"),
        ("hna_recommendations", "recommendations"),
    ];

    // Survey column suffix → the catalogue's StakeholderItems value (the quarterly WhoWith values).
    private static readonly (string Suffix, string Item)[] EngagementItems =
    [
        ("lived_experience", "lived_experience"),
        ("licensing", "licensing"),
        ("housing", "housing"),
        ("alcohol_services", "alcohol"),
        ("drug_treatment_services", "drug"),
        ("mental_health_services", "mental_health"),
        ("police", "police"),
        ("vcse", "vcse"),
        ("welfare_services", "welfare"),
        ("debt_advice", "debt"),
        ("icb_representative", "icb"),
    ];

    private static readonly (string Suffix, string Item)[] SteeringItems =
    [
        ("lived_experience", "lived_experience"),
        ("licensing", "licensing"),
        ("housing", "housing"),
        ("alcohol_services", "alcohol"),
        ("drug_treatment_services", "drug"),
        ("mental_health_services", "mental_health"),
        ("police", "police"),
        ("vcse", "vcse"),
        ("welfare_services", "welfare"),
        ("debt_advice", "debt"),
        ("dhsc_regional_lead", "dhsc_regional_lead"),
        ("la_public_health_rep", "la_public_health"),
        ("icb_rep", "icb"),
    ];

    private static readonly (string Suffix, string Item)[] TrainingItems =
    [
        ("licensing", "licensing"),
        ("housing", "housing"),
        ("alcohol_services", "alcohol"),
        ("drug_treatment_services", "drug"),
        ("mental_health_services", "mental_health"),
        ("police", "police"),
        ("welfare_services", "welfare"),
        ("debt_advice", "debt"),
        ("local_politicians", "local_politicians"),
        ("coroners", "coroners"),
        ("teachers_schools", "teachers_schools"),
        ("primary_care", "primary_care"),
    ];

    private static readonly (string Suffix, string Item)[] ScreeningItems =
    [
        ("licensing", "licensing"),
        ("housing", "housing"),
        ("alcohol_services", "alcohol"),
        ("drug_treatment_services", "drug"),
        ("mental_health_services", "mental_health"),
        ("police", "police"),
        ("welfare_services", "welfare"),
        ("debt_advice", "debt"),
        ("primary_care", "primary_care"),
    ];

    private static readonly (string Suffix, string Item)[] StrategyItems =
    [
        ("drugs", "drugs"),
        ("alcohol", "alcohol"),
        ("mental_health", "mental_health"),
        ("suicide", "suicide"),
    ];

    public static IReadOnlyList<MaturityBaselineMappedAnswer> Map(MaturityBaselineRow row) => Conform(MapRaw(row));

    // The transcription before catalogue conformance — exposed so a test can prove Conform drops nothing.
    public static IReadOnlyList<MaturityBaselineMappedAnswer> MapRaw(MaturityBaselineRow row)
    {
        var mapper = new RowMapper(row);

        // LA capacity
        mapper.Radio("designated_gambling_harm_lead", "MA_DESIGNATED_LEAD", YesNo);
        mapper.NonFundedRoles();
        mapper.Text("other_roles_wte", "MA_OTHER_ROLES");
        mapper.BusinessPlan();
        mapper.Radio("part_of_strategic_network", "MA_STRATEGIC_NETWORK", StrategicNetwork);
        mapper.Text("strategic_network_details", "MA_STRATEGIC_NETWORK_DETAILS");
        mapper.RadioOrOther("consider_pooling_resources", "MA_SHARED_RESOURCES", SharedResources);

        // Needs assessment
        mapper.Radio("hna_published", "MA_NA_PUBLISHED", YesNoInProgress);
        mapper.Text("hna_link", "MA_NA_URL");
        mapper.TickedColumns(CoverageColumns, "MA_NA_COVERAGE");
        mapper.LastPublishedYear("hna_latest_published", "MA_NA_LAST_PUBLISHED");
        mapper.NextDueYear("hna_next_due", "MA_NA_NEXT_DUE");
        mapper.Radio("hna_commissioned_new_research", "MA_NA_RESEARCH", YesNoDontKnow);
        mapper.Text("hna_research_details", "MA_NA_RESEARCH_DETAILS");
        mapper.Matrix("stakeholder_", EngagementItems, "MA_NA_ENGAGEMENT", Engagement);
        mapper.Text("stakeholder_other", "MA_NA_ENGAGEMENT_OTHER");
        mapper.Text("lived_experience_details", "MA_NA_LIVED_EXPERIENCE");
        mapper.Radio("want_regional_hna", "MA_NA_REGIONAL", RegionalHna);
        mapper.Text("regional_hna_improvements", "MA_NA_REGIONAL_IMPROVE");

        // Strategic approach
        mapper.Radio("steering_group_status", "MA_SG_EXISTS", SteeringGroup);
        mapper.Matrix("steering_", SteeringItems, "MA_SG_MEMBERS", SteeringMember);
        mapper.Text("steering_other_members", "MA_SG_MEMBERS_OTHER");
        mapper.RadioOrOther("action_plan_status", "MA_ACTION_PLAN", YesNoInProgress);
        mapper.Matrix("strategy_", StrategyItems, "MA_STRATEGIES", Strategy);
        mapper.Text("embedded_in_other_strategies", "MA_STRATEGIES_OTHER");
        mapper.RadioOrOther("presented_to_health_and_wellbeing_board", "MA_HWB_PRESENTED", YesNo);
        mapper.Radio("last_presented_to_hwb", "MA_HWB_LAST", HwbYears);
        mapper.Radio("workplace_policies_for_staff", "MA_STAFF_SUPPORT", StaffSupport);

        // Awareness, training and signposting
        mapper.Text("training_needed", "MA_TRAINING_NEEDS");
        mapper.Matrix("training_commissioned_", TrainingItems, "MA_TRAINED_WORKFORCES", Trained);
        mapper.Text("training_commissioned_other_workforces", "MA_TRAINED_OTHER");
        mapper.Matrix("screening_tool_", ScreeningItems, "MA_SCREENING", Screening);
        mapper.Text("other_services_use_screening_tool", "MA_SCREENING_OTHER");
        mapper.Radio("treatment_support_access_process", "MA_REFERRAL_PATHWAY", Referral);

        // Planning and licensing
        mapper.Text("policy_statement_link", "MA_LIC_POLICY_URL");
        mapper.Radio("ph_involved_policy_development", "MA_LIC_PH_INVOLVED", YesNoDontKnow);
        mapper.Radio("policy_includes_local_area_profile", "MA_LIC_LOCAL_PROFILE", LocalProfile);
        mapper.Text("ph_factors_in_local_area_profile", "MA_LIC_PROFILE_FACTORS");
        mapper.Text("ph_involvement_other_methods", "MA_LIC_OTHER_WAYS");
        mapper.Radio("premises_restricted_refused_due_ph", "MA_LIC_RESTRICTED", YesNoDontKnow);
        mapper.Radio("ph_invited_licensing_representations", "MA_LIC_INVITED", YesNoDontKnow);
        mapper.RadioOrOther("harms_in_planning_guidance", "MA_PLAN_GUIDANCE", YesNoDontKnow);
        mapper.Radio("ph_invited_planning_representations", "MA_PLAN_INVITED", YesNoDontKnow);

        // Local campaigns
        mapper.RadioOrOther("awareness_campaign_past_year", "MA_CAMPAIGN", Campaign);
        mapper.Text("campaign_target_audiences", "MA_CAMPAIGN_AUDIENCE");
        mapper.SemicolonList("campaign_aims", "MA_CAMPAIGN_AIMS", CampaignAims, allOfTheAbove: true);
        mapper.Text("campaign_resources_link", "MA_CAMPAIGN_URL");

        // Restriction of local advertising
        mapper.Radio("ad_restrictions_gambling_council_spaces", "MA_ADV_RESTRICTED", AdvertisingRestricted);
        mapper.Text("ad_restrictions_introduced_date", "MA_ADV_DATE");
        mapper.RadioOrOther("ad_restrictions_type", "MA_ADV_TYPE", AdvertisingType);
        mapper.RadioOrOther("ad_restrictions_apply_all_council_property", "MA_ADV_ALL_PROPERTY", YesNoDontKnow);
        mapper.Text("ad_restrictions_exempt_property", "MA_ADV_EXEMPT");
        mapper.Radio("ad_restrictions_other_commodities", "MA_ADV_OTHER_COMMODITIES", OtherCommodities);

        // Working with VCSE
        mapper.Radio("commissioning_vcse_for_prevention", "MA_VCSE_COMMISSIONING", YesNoDontKnow);
        mapper.SemicolonList("commissioned_vcse_names", "MA_VCSE_COMMISSIONED", VcseOrganisations);
        mapper.Text("commissioned_vcse_contract_details", "MA_VCSE_CONTRACTS");
        mapper.SemicolonList("commissioned_vcse_services", "MA_VCSE_SERVICES", VcseServices);
        mapper.Radio("engage_noncommissioned_vcse", "MA_VCSE_ENGAGED", YesNoDontKnow);
        mapper.SemicolonList("engaged_vcse_names", "MA_VCSE_ENGAGED_NAMES", VcseOrganisations);
        mapper.SemicolonList("engaged_vcse_services", "MA_VCSE_ENGAGED_SERVICES", VcseServices);

        // Other preventative services / next steps
        mapper.Radio("other_prevention_services", "MA_OTHER_SERVICES", OtherServices);
        mapper.Text("other_prevention_services_details", "MA_OTHER_SERVICES_DETAILS");
        mapper.Text("priorities_preventing_gambling_harm", "MA_PRIORITIES");

        return mapper.Answers;
    }

    // Keeps only codes the catalogue knows and, for choice questions, only option values it offers. A radio that
    // somehow carries two values is dropped rather than guessed. Blank free text is dropped.
    public static IReadOnlyList<MaturityBaselineMappedAnswer> Conform(IReadOnlyList<MaturityBaselineMappedAnswer> answers)
    {
        var questions = MaturityQuestionResolver.ResolveAll().ToDictionary(q => q.Code, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<MaturityBaselineMappedAnswer>();

        foreach (var answer in answers)
        {
            if (!questions.TryGetValue(answer.Code, out var question) || !seen.Add(answer.Code) || question.IsRoles)
            {
                continue;
            }

            if (!MaturityFieldTypes.IsChoice(question.QuestionType))
            {
                if (!string.IsNullOrWhiteSpace(answer.Value))
                {
                    result.Add(answer);
                }

                continue;
            }

            var allowed = question.Options.Select(o => o.Value).ToHashSet(StringComparer.Ordinal);
            var tokens = answer.Value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(allowed.Contains)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (tokens.Count == 0 || (tokens.Count > 1 && question.QuestionType != MaturityFieldTypes.Checkbox))
            {
                continue;
            }

            result.Add(new MaturityBaselineMappedAnswer(answer.Code, string.Join(",", tokens)));
        }

        return result;
    }

    // Survey wording → catalogue value, matched case-insensitively after trimming, collapsing whitespace, folding
    // curly apostrophes and dropping a trailing ";" or ".".
    internal static string Key(string value)
    {
        var key = value.Trim().Replace('’', '\'');
        key = Whitespace().Replace(key, " ").TrimEnd(';', '.', ' ').ToLowerInvariant();
        return key;
    }

    private static IReadOnlyDictionary<string, string> Table(params (string Value, string Label)[] entries)
    {
        var table = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (value, label) in entries)
        {
            table[Key(label)] = value;
        }

        return table;
    }

    private static IReadOnlyDictionary<string, string> LabelTable(
        IReadOnlyList<Application.Submissions.Protocol.ResolvedQuestionOption> options,
        params (string Value, string Label)[] aliases)
    {
        var table = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var option in options.Where(o => o.Value != Other))
        {
            table[Key(option.Label)] = option.Value;
        }

        foreach (var (value, label) in aliases)
        {
            table[Key(label)] = value;
        }

        return table;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    private sealed class RowMapper(MaturityBaselineRow row)
    {
        public List<MaturityBaselineMappedAnswer> Answers { get; } = [];

        // The trimmed cell, or null for a blank or one of the survey's "not answered" spellings.
        public string? Raw(string column)
        {
            if (!row.Answers.TryGetValue(column, out var value))
            {
                return null;
            }

            var trimmed = value.Trim();
            return trimmed.Length == 0 || NotAnswered.Contains(trimmed.TrimEnd('.')) ? null : trimmed;
        }

        public void Add(string code, string value) => Answers.Add(new MaturityBaselineMappedAnswer(code, value));

        public void Text(string column, string code)
        {
            if (Raw(column) is { } value)
            {
                Add(code, value);
            }
        }

        public void Radio(string column, string code, IReadOnlyDictionary<string, string> table)
        {
            if (Raw(column) is { } value && table.TryGetValue(Key(value), out var mapped))
            {
                Add(code, mapped);
            }
        }

        // An answer outside the option set is the respondent's own words: keep it as Other + the companion text.
        public void RadioOrOther(string column, string code, IReadOnlyDictionary<string, string> table)
        {
            if (Raw(column) is not { } value)
            {
                return;
            }

            if (table.TryGetValue(Key(value), out var mapped))
            {
                Add(code, mapped);
                return;
            }

            Add(code, Other);
            Add($"{code}_OTHER", value.TrimEnd(';').Trim());
        }

        public void Matrix(string columnPrefix, (string Suffix, string Item)[] items, string codePrefix, IReadOnlyDictionary<string, string> table)
        {
            foreach (var (suffix, item) in items)
            {
                Radio(columnPrefix + suffix, $"{codePrefix}__{item.ToUpperInvariant()}", table);
            }
        }

        // A checkbox spread across one Yes/No column per option: "Yes" ticks it.
        public void TickedColumns((string Column, string Value)[] columns, string code)
        {
            var ticked = columns
                .Where(c => Raw(c.Column) is { } value && Key(value) == "yes")
                .Select(c => c.Value)
                .ToList();

            if (ticked.Count > 0)
            {
                Add(code, string.Join(",", ticked));
            }
        }

        // Microsoft Forms multi-select: "A;B;C;". Known labels become values; anything else becomes Other plus the
        // companion text so a locally-named organisation or service is not lost. "All of the above" ticks every
        // non-Other option where the question allows it.
        public void SemicolonList(string column, string code, IReadOnlyDictionary<string, string> table, bool allOfTheAbove = false)
        {
            if (Raw(column) is not { } value)
            {
                return;
            }

            var parts = value
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(part => !NotAnswered.Contains(part.TrimEnd('.')))
                .ToList();

            var values = new List<string>();
            var unknown = new List<string>();
            foreach (var part in parts)
            {
                if (table.TryGetValue(Key(part), out var mapped))
                {
                    values.Add(mapped);
                }
                else if (allOfTheAbove && Key(part) == "all of the above")
                {
                    values.AddRange(table.Values);
                }
                else
                {
                    unknown.Add(part);
                }
            }

            if (unknown.Count > 0)
            {
                values.Add(Other);
            }

            values = values.Distinct(StringComparer.Ordinal).ToList();
            if (values.Count == 0)
            {
                return;
            }

            Add(code, string.Join(",", values));
            if (unknown.Count > 0)
            {
                Add($"{code}_OTHER", string.Join("; ", unknown));
            }
        }

        // The survey asked one WTE band per role, and so does the assessment (BLUM-7924): a role is ticked when its
        // band is above zero, and that band becomes the role's own WTE answer. A zero-band role is left unticked, so
        // it gets no WTE — its question would be hidden anyway.
        public void NonFundedRoles()
        {
            var answered = false;
            var ticked = new List<(string Role, string Band)>();

            foreach (var (column, role) in WteColumns)
            {
                if (Raw(column) is not { } value)
                {
                    continue;
                }

                var key = value.Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
                if (!WteBandValues.TryGetValue(key, out var band))
                {
                    continue;
                }

                answered = true;
                if (band != NoWte)
                {
                    ticked.Add((role, band));
                }
            }

            if (!answered)
            {
                return;
            }

            if (ticked.Count == 0)
            {
                Add("MA_NONFUNDED_ROLES", MaturityOptionValues.None);
                return;
            }

            Add("MA_NONFUNDED_ROLES", string.Join(",", ticked.Select(t => t.Role)));
            foreach (var (role, band) in ticked)
            {
                Add(MaturityProtocol.NonFundedWteCode(role), band);
            }
        }

        // "Yes;" / "No;" are the two tick-box answers; anything else — including "Yes;No;" and every free-text
        // qualification — is the respondent's own explanation.
        public void BusinessPlan()
        {
            if (Raw("business_plan_includes_gambling_harm_fy2526") is not { } value)
            {
                return;
            }

            var parts = value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 1 && YesNo.TryGetValue(Key(parts[0]), out var mapped))
            {
                Add("MA_BUSINESS_PLAN", mapped);
                return;
            }

            Add("MA_BUSINESS_PLAN", Other);
            Add("MA_BUSINESS_PLAN_OTHER", value.TrimEnd(';').Trim());
        }

        public void LastPublishedYear(string column, string code)
        {
            if (Raw(column) is not { } value)
            {
                return;
            }

            if (Key(value) == "pre 2020")
            {
                Add(code, MaturityOptionValues.Pre2020);
                return;
            }

            if (TryParseYear(value, out var year))
            {
                if (year < 2020)
                {
                    Add(code, MaturityOptionValues.Pre2020);
                }
                else if (year <= 2026)
                {
                    Add(code, year.ToString(CultureInfo.InvariantCulture));
                }
            }
        }

        public void NextDueYear(string column, string code)
        {
            if (Raw(column) is not { } value)
            {
                return;
            }

            if (Key(value) == "no date set")
            {
                Add(code, MaturityOptionValues.NoDateSet);
                return;
            }

            if (TryParseYear(value, out var year))
            {
                if (year > 2030)
                {
                    Add(code, MaturityOptionValues.Post2030);
                }
                else if (year >= 2026)
                {
                    Add(code, year.ToString(CultureInfo.InvariantCulture));
                }
            }
        }

        // The export writes years as "2025" or, straight from Excel, "2025.0".
        private static bool TryParseYear(string value, out int year)
        {
            if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) && number == decimal.Truncate(number))
            {
                year = (int)number;
                return year is >= 1900 and <= 2100;
            }

            year = 0;
            return false;
        }
    }
}