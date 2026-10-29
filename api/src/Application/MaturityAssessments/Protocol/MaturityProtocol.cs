using Application.Submissions.Protocol;

#pragma warning disable SA1116, SA1117 // Parameter-layout rules: this is a pure data file — the compact
// one-row-per-question layout (positional core + named extras) reads far better than one parameter per line.

namespace Application.MaturityAssessments.Protocol;

// A single maturity-assessment question as declared by the catalogue. Codes are UPPER_SNAKE with an MA_ prefix;
// matrix items are generated as {PARENT}__{ITEM}. Options carry stored values + display labels. A question with a
// ConditionalQuestionCode renders inside its parent's reveal and is only required/persisted when visible; a page
// with VisibleWhen is skipped wholesale unless the named answer holds one of the listed values.
public sealed record MaturityQuestionSpec(
    string Code,
    string Label,
    string FieldType,
    string? Hint = null,
    IReadOnlyList<ResolvedQuestionOption>? Options = null,
    bool IsRequired = true,
    string? ConditionalQuestionCode = null,
    string? ConditionalValue = null,
    string? GroupLabel = null,
    string? PrefillSource = null,
    string? ErrorMessage = null,
    string? ExclusiveOptionValue = null,
    IReadOnlyList<ResolvedQuestionOption>? SecondaryOptions = null);

public sealed record MaturityPageVisibility(string QuestionCode, IReadOnlyList<string> Values);

public sealed record MaturityPageSpec(
    string Code,
    string Title,
    IReadOnlyList<MaturityQuestionSpec> Questions,
    MaturityPageVisibility? VisibleWhen = null);

public sealed record MaturitySectionSpec(string Code, string Title, IReadOnlyList<MaturityPageSpec> Pages);

// Static transcription of "MA & QP data mapping 08092026.xlsx" (sheet "Full MA to QP mapping"): the ten
// maturity-assessment domains (column B), their pages (column D), the content-reviewed question copy (column F,
// falling back to E), hints (G), answer options (H/I) and conditionality (J). A static catalogue rather than a
// seeded table on purpose: DHSC does not edit it, submitted assessments freeze their own rendered snapshot, and a
// copy change here is a code change with no reseed migration and no boot-time crash-loop risk.
//
// Conventions:
// - Option VALUES are snake_case tokens (MaturityOptionValues); LABELS are the mapping-file / Figma copy.
// - Three lists are pinned equal to the quarterly protocol's values by MaturityProtocolTests because the pre-fill
//   engine copies those values 1:1: StakeholderItems == KeyResultProtocolSeedData.WhoWithOptions,
//   SteeringGroupEngagement == its EngagementLevels, FundedTimeOptions == the seeded PR_FUNDING_TIME options.
// - Every option list containing "other" has a {CODE}_OTHER free-text companion conditional on it.
// - Free text carries no protocol character limit (MaxLength is null), so the client shows no GDS character count
//   (BLUM-8411). Every answer is still capped at FieldLimits.AnswerText (10,000) on a draft save and Continue
//   alike; the client checks the same ceiling as MATURITY_ANSWER_MAX_LENGTH.
// - "PrefillSource: quarterly" marks a GREEN row of the mapping file — QuarterlyPrefillRules must know its code.
// - Optional questions carry " (optional)" in the label: SummaryList derives row testids from the label, and the
//   GDS marker belongs in the seeded copy rather than in a renderer rule (BLUM-7479).
// - Design discrepancies resolved (flagged in the PR): the mapping file wins on option sets Figma trimmed; Figma
//   wins on the funded-roles repeater with the quarterly percentage bands, and on the Publishing page layout.
public static class MaturityProtocol
{
    public const string QuarterlyPrefill = "quarterly";

    public const string OptionalSuffix = " (optional)";

    // ---- Shared option lists ------------------------------------------------------------------------------

    public static readonly IReadOnlyList<ResolvedQuestionOption> YesNo =
    [
        new(MaturityOptionValues.Yes, "Yes"),
        new(MaturityOptionValues.No, "No"),
    ];

    public static readonly IReadOnlyList<ResolvedQuestionOption> YesNoDontKnow =
    [
        new(MaturityOptionValues.Yes, "Yes"),
        new(MaturityOptionValues.No, "No"),
        new(MaturityOptionValues.DontKnow, "Don't know"),
    ];

    // The 23 stakeholder items every "who" matrix asks about — the quarterly WhoWithOptions values, in its order.
    public static readonly IReadOnlyList<ResolvedQuestionOption> StakeholderItems =
    [
        new("lived_experience", "People with lived experience"),
        new("affected_others", "Affected others"),
        new("licensing", "Licensing"),
        new("planning", "Planning"),
        new("housing", "Housing"),
        new("children", "Children and youth services"),
        new("alcohol", "Alcohol services"),
        new("drug", "Drug treatment services"),
        new("mental_health", "Mental health services"),
        new("police", "Police"),
        new("vcse", "Voluntary, community and social enterprises (VCSEs)"),
        new("welfare", "Welfare services"),
        new("debt", "Debt advice"),
        new("primary_care", "Primary care"),
        new("icb", "Integrated care board (ICB)"),
        new("other_health_practitioners", "Other health practitioners"),
        new("criminal_justice", "Criminal justice system staff"),
        new("dhsc_regional_lead", "DHSC regional lead for gambling"),
        new("la_public_health", "Local authority public health representative"),
        new("local_politicians", "Local politicians"),
        new("coroners", "Coroners"),
        new("teachers_schools", "Teachers or schools"),
        new("other", "Other"),
    ];

    public static readonly IReadOnlyList<ResolvedQuestionOption> EngagementOptions =
    [
        new(MaturityOptionValues.Yes, "Yes"),
        new(MaturityOptionValues.No, "No"),
        new(MaturityOptionValues.ContactedNoResponse, "Contacted but no response"),
        new(MaturityOptionValues.DontKnow, "Do not know"),
    ];

    // Values are the quarterly EngagementLevels; labels are the maturity survey's (Figma 2744-183090).
    public static readonly IReadOnlyList<ResolvedQuestionOption> SteeringGroupEngagement =
    [
        new(MaturityOptionValues.AttendsEngages, "Attends and engages"),
        new(MaturityOptionValues.AttendsLimited, "Attends but limited engagement"),
        new(MaturityOptionValues.ContactedNoResponse, "Contacted but no response"),
        new(MaturityOptionValues.NoEngagement, "No"),
        new(MaturityOptionValues.DoNotKnow, "Do not know"),
    ];

    public static readonly IReadOnlyList<ResolvedQuestionOption> TrainedOptions =
    [
        new(MaturityOptionValues.Yes, "Yes"),
        new(MaturityOptionValues.No, "No"),
        new(MaturityOptionValues.InProgress, "In progress"),
        new(MaturityOptionValues.DontKnow, "Don't know"),
    ];

    public static readonly IReadOnlyList<ResolvedQuestionOption> StrategyItems =
    [
        new("drugs", "Drugs"),
        new("alcohol", "Alcohol"),
        new("mental_health", "Mental health"),
        new("suicide", "Suicide"),
    ];

    public static readonly IReadOnlyList<ResolvedQuestionOption> StrategyOptions =
    [
        new(MaturityOptionValues.Yes, "Yes"),
        new(MaturityOptionValues.No, "No"),
        new(MaturityOptionValues.NoStrategy, "Do not have a strategy on this topic area"),
        new(MaturityOptionValues.InDevelopment, "In development"),
    ];

    public static readonly IReadOnlyList<ResolvedQuestionOption> RoleOptions =
    [
        new("public_health_officer", "Public Health Officer"),
        new("public_health_analyst", "Public Health Analyst"),
        new("public_health_practitioner", "Public Health Practitioner"),
        new("public_health_specialist", "Public Health Specialist"),
        new("public_health_intelligence_specialist", "Public Health Intelligence Specialist"),
        new("consultant_in_public_health", "Consultant in Public Health"),
    ];

    // "None" is the mapping file's answer for an LA with no levy-funded role (BLUM-9112). MA_FUNDED_ROLES declares it
    // its ExclusiveOptionValue, which is what makes it the single row's role with no time (MaturityRolesAnswer).
    public static readonly IReadOnlyList<ResolvedQuestionOption> FundedRoleOptions =
        [.. RoleOptions, new(MaturityOptionValues.Other, "Other role"), new(MaturityOptionValues.None, "None")];

    public static readonly IReadOnlyList<ResolvedQuestionOption> NonFundedRoleOptions =
        [.. RoleOptions, new(MaturityOptionValues.None, "None")];

    // The seeded PR_FUNDING_TIME option values (ApplicationDbContextInitialiser.SeedFormDesigner), so a funded
    // role's time copies from the activity record untranslated. Labels follow Figma 2744-182477.
    public static readonly IReadOnlyList<ResolvedQuestionOption> FundedTimeOptions =
    [
        new("0", "0%"),
        new("up_to_20", "Up to 20%"),
        new("21_40", "21% to 40%"),
        new("41_60", "41% to 60%"),
        new("61_80", "61% to 80%"),
        new("81_100", "81% to 100%"),
    ];

    public static readonly IReadOnlyList<ResolvedQuestionOption> WteBands =
    [
        new("0", "0 WTE"),
        new("up_to_0_2", ">0–0.2 WTE"),
        new("0_2_to_0_4", ">0.2–0.4 WTE"),
        new("0_4_to_0_6", ">0.4–0.6 WTE"),
        new("0_6_to_0_8", ">0.6–0.8 WTE"),
        new("0_8_to_1", ">0.8–1 WTE"),
        new("over_1", ">1 WTE"),
    ];

    // The quarterly NeedsAssessmentInformation values (minus lived_experience/other, which have no maturity item)
    // plus the survey-only "Stakeholder engagement". Labels from Figma 2744-183019.
    public static readonly IReadOnlyList<ResolvedQuestionOption> NeedsAssessmentCoverage =
    [
        new("prevalence", "Prevalence of gambling-related harms"),
        new("specific_groups", "Data on specific groups"),
        new("venue_locations", "Location of gambling premises"),
        new("service_map", "Mapping of your current service provision, like prevention, treatment and support services in your area"),
        new("future", "Opportunities and challenges"),
        new("recommendations", "Recommendations"),
        new("stakeholder_engagement", "Stakeholder engagement"),
    ];

    public static readonly IReadOnlyList<ResolvedQuestionOption> LastPublishedYears =
    [
        new(MaturityOptionValues.Pre2020, "Pre 2020"),
        new("2020", "2020"),
        new("2021", "2021"),
        new("2022", "2022"),
        new("2023", "2023"),
        new("2024", "2024"),
        new("2025", "2025"),
        new("2026", "2026"),
    ];

    public static readonly IReadOnlyList<ResolvedQuestionOption> NextDueYears =
    [
        new("2026", "2026"),
        new("2027", "2027"),
        new("2028", "2028"),
        new("2029", "2029"),
        new("2030", "2030"),
        new(MaturityOptionValues.Post2030, "Post 2030"),
        new(MaturityOptionValues.NoDateSet, "No date set"),
    ];

    public static readonly IReadOnlyList<ResolvedQuestionOption> RegionalHnaOptions =
    [
        new(MaturityOptionValues.Yes, "Yes"),
        new(MaturityOptionValues.No, "No"),
        new(MaturityOptionValues.Maybe, "Maybe"),
        new(MaturityOptionValues.HaveAndUse, "We already have one that we use"),
        new(MaturityOptionValues.HaveDontUse, "We already have one that we don't use"),
        new(MaturityOptionValues.NeedsUpdating, "Our regional HNA needs updating"),
        new(MaturityOptionValues.DontKnow, "Don't know"),
    ];

    public static readonly IReadOnlyList<ResolvedQuestionOption> HwbYears =
    [
        new("fy_2025_26", "FY 2025/26"),
        new("fy_2024_25", "FY 2024/25"),
        new("fy_2023_24", "FY 2023/24"),
        new("fy_2022_23", "FY 2022/23"),
        new("prior_fy_2022_23", "Prior to FY 2022/23"),
    ];

    public static readonly IReadOnlyList<ResolvedQuestionOption> CampaignAims =
    [
        new("promoting_help", "Promoting help/support"),
        new("reducing_stigma", "Reducing stigma"),
        new("awareness_raising", "Awareness raising of gambling related harms"),
        new(MaturityOptionValues.Other, "Other"),
    ];

    public static readonly IReadOnlyList<ResolvedQuestionOption> VcseOrganisations =
    [
        new("addiction_family_support", "Addiction Family Support"),
        new("adferiad", "Adferiad"),
        new("against_the_odds", "Against The Odds Education & Awareness"),
        new("beacon_counselling_trust", "Beacon Counselling Trust"),
        new("behavioural_insights_team", "Behavioural Insights Team"),
        new("betknowmore_uk", "Betknowmore UK"),
        new("chapter_one", "Chapter One, part of the charity Gambling with Lives"),
        new("citizens_advice", "Citizens Advice"),
        new("epic_restart_foundation", "EPIC Restart Foundation"),
        new("gamban", "Gamban"),
        new("gambling_harm_uk", "Gambling Harm UK"),
        new("gambling_lived_experience_network", "Gambling Lives Experience Network"),
        new("gambling_with_lives", "Gambling with Lives"),
        new("gamcare", "GamCare"),
        new("gamfam", "GamFam"),
        new("gamlearn", "GamLEARN"),
        new("gordon_moody", "Gordon Moody"),
        new("nhs_addiction_providers_alliance", "NHS Addiction Providers Alliance"),
        new("rcgp", "Royal College of General Practitioners"),
        new("samaritans", "Samaritans"),
        new("cega", "The Coalition to End Gambling Ads (CEGA)"),
        new("white_ribbon_association", "White Ribbon Association"),
        new("ygam", "Ygam"),
        new("gambleaware", "GambleAware"),
        new(MaturityOptionValues.Other, "Other"),
    ];

    public static readonly IReadOnlyList<ResolvedQuestionOption> VcseServices =
    [
        new("training_frontline", "Training for frontline workers"),
        new("education_cyp", "Education interventions for children and young people"),
        new("community_outreach", "Community outreach and social action"),
        new("affected_others", "Support for affected others"),
        new("blocking_tools", "Blocking tools"),
        new(MaturityOptionValues.Other, "Other"),
    ];

    private static readonly IReadOnlyList<ResolvedQuestionOption> YesNoOther =
    [
        new(MaturityOptionValues.Yes, "Yes"),
        new(MaturityOptionValues.No, "No"),
        new(MaturityOptionValues.Other, "Other"),
    ];

    private static readonly IReadOnlyList<ResolvedQuestionOption> YesNoDontKnowOther =
    [
        new(MaturityOptionValues.Yes, "Yes"),
        new(MaturityOptionValues.No, "No"),
        new(MaturityOptionValues.DontKnow, "Don't know"),
        new(MaturityOptionValues.Other, "Other"),
    ];

    // ---- The catalogue -----------------------------------------------------------------------------------

    public static IReadOnlyList<MaturitySectionSpec> Sections { get; } = BuildSections();

    public static IReadOnlyList<string> SectionCodes { get; } = Sections.Select(s => s.Code).ToList();

    public static MaturitySectionSpec? FindSection(string sectionCode) =>
        Sections.FirstOrDefault(s => string.Equals(s.Code, sectionCode, StringComparison.Ordinal));

    public static MaturityPageSpec? FindPage(string sectionCode, string pageCode) =>
        FindSection(sectionCode)?.Pages.FirstOrDefault(p => string.Equals(p.Code, pageCode, StringComparison.Ordinal));

    // The per-role WTE question a ticked non-funded role reveals — shared with the 2025 baseline mapping so the code
    // is spelled in one place.
    public static string NonFundedWteCode(string roleValue) => $"MA_NONFUNDED_WTE__{roleValue.ToUpperInvariant()}";

    private static IReadOnlyList<MaturitySectionSpec> BuildSections()
    {
        const string yes = MaturityOptionValues.Yes;
        const string other = MaturityOptionValues.Other;
        const string radio = MaturityFieldTypes.Radio;
        const string checkbox = MaturityFieldTypes.Checkbox;
        const string select = MaturityFieldTypes.Select;
        const string text = MaturityFieldTypes.Text;
        const string textarea = MaturityFieldTypes.Textarea;

        // Sheet row 102: the answers that send the LA on to the campaign follow-ups (rows 103-105). "Other" joined
        // "Yes" and "In progress" in BLUM-8438; "No" and "Don't know" skip them.
        const string campaignFollowUpAnswers = $"{yes},{MaturityOptionValues.InProgress},{other}";

        return
        [
            new("la_capacity", "LA capacity",
            [
                new("people_and_roles", "People and roles",
                [
                    new("MA_DESIGNATED_LEAD", "Does your public health team currently have designated members responsible for gambling harms prevention?", radio, Options: YesNo),
                ]),
                new("non_funded_roles", "Non-funded roles",
                [
                    new("MA_NONFUNDED_ROLES", "Which roles are not funded by the levy?", checkbox, Hint: "Select all that apply",
                        Options: NonFundedRoleOptions, ExclusiveOptionValue: MaturityOptionValues.None),
                    .. NonFundedWte(),
                ]),
                new("funded_roles", "Funded roles",
                [
                    new("MA_FUNDED_ROLES", "Which roles are being funded by the levy?", MaturityFieldTypes.Roles,
                        Options: FundedRoleOptions, SecondaryOptions: FundedTimeOptions, PrefillSource: QuarterlyPrefill,
                        ErrorMessage: "Add at least one role funded by the levy", ExclusiveOptionValue: MaturityOptionValues.None),
                    Optional("MA_OTHER_ROLES", "Please highlight any other roles within the public health team that are not listed above and are currently working on gambling harms prevention", textarea,
                        hint: "Please also include WTE information", prefillSource: QuarterlyPrefill),
                ]),
                new("business_plan", "Business plan",
                [
                    new("MA_BUSINESS_PLAN", "Is gambling harms prevention included in your local business plan?", radio, Options: YesNoOther),
                    OtherText("MA_BUSINESS_PLAN", "How is gambling harms prevention included in your local business plan?"),
                ]),
                new("strategic_network", "Strategic network",
                [
                    new("MA_STRATEGIC_NETWORK", "Is your local authority part of a wider strategic network working together to prevent gambling harms?", radio,
                        Options:
                        [
                            new(yes, "Yes"),
                            new(MaturityOptionValues.No, "No"),
                            new(MaturityOptionValues.WouldLikeTo, "No - but we would like to be"),
                            new(MaturityOptionValues.NotSure, "Not sure"),
                        ],
                        PrefillSource: QuarterlyPrefill),
                    new("MA_STRATEGIC_NETWORK_DETAILS", "What is the strategic network your local authority participates in?", textarea,
                        Hint: "Include the name of the network, the participating local authorities, and the areas you collaborate on",
                        ConditionalQuestionCode: "MA_STRATEGIC_NETWORK", ConditionalValue: yes),
                ]),
                new("shared_resources", "Shared resources",
                [
                    new("MA_SHARED_RESOURCES", "Is your local authority considering contributing allocated resources to shared regional or network level capacity?", radio,
                        Options:
                        [
                            new(yes, "Yes"),
                            new(MaturityOptionValues.No, "No"),
                            new(MaturityOptionValues.Considering, "Considering but undecided"),
                            new(MaturityOptionValues.DontKnow, "Do not know"),
                            new(other, "Other"),
                        ]),
                    OtherText("MA_SHARED_RESOURCES", "In what other ways is your local authority considering contributing allocated resources?"),
                ]),
            ]),

            new("needs_assessment", "Needs assessment",
            [
                new("publishing", "Publishing",
                [
                    new("MA_NA_PUBLISHED", "Has your local authority published a needs assessment for gambling harms in your area?", radio,
                        Hint: "This might be a local health or joint strategic needs assessment, or other type of needs assessment",
                        Options:
                        [
                            new(yes, "Yes"),
                            new(MaturityOptionValues.No, "No"),
                            new(MaturityOptionValues.InProgress, "In progress"),
                        ],
                        PrefillSource: QuarterlyPrefill),
                    new("MA_NA_URL", "What is the web address of the published assessment?", text,
                        Hint: "Please say N/A if the assessment is not published online.",
                        ConditionalQuestionCode: "MA_NA_PUBLISHED", ConditionalValue: yes, PrefillSource: QuarterlyPrefill),
                    new("MA_NA_COVERAGE", "What information does the assessment cover?", checkbox, Options: NeedsAssessmentCoverage,
                        ConditionalQuestionCode: "MA_NA_PUBLISHED", ConditionalValue: yes, PrefillSource: QuarterlyPrefill),
                ]),
                new("publishing_dates", "Publishing dates",
                [
                    new("MA_NA_LAST_PUBLISHED", "When was your last needs assessment published?", select, Options: LastPublishedYears),
                    new("MA_NA_NEXT_DUE", "When will your next needs assessment be published?", select, Options: NextDueYears),
                ], VisibleWhen: new("MA_NA_PUBLISHED", [yes])),
                new("research", "Research",
                [
                    new("MA_NA_RESEARCH", "Did you commission any research when creating your needs assessment?", radio, Options: YesNoDontKnow),
                    new("MA_NA_RESEARCH_DETAILS", "What research did you commission?", textarea,
                        ConditionalQuestionCode: "MA_NA_RESEARCH", ConditionalValue: yes),
                ], VisibleWhen: new("MA_NA_PUBLISHED", [yes])),
                new("engagement", "Engagement",
                [
                    .. Matrix("MA_NA_ENGAGEMENT", "Who did you engage with when creating your needs assessment?", StakeholderItems, EngagementOptions, QuarterlyPrefill),
                    Optional("MA_NA_ENGAGEMENT_OTHER", "Have you engaged or plan to engage any other stakeholders?", textarea,
                        hint: "List any other stakeholders who help inform your needs assessment", prefillSource: QuarterlyPrefill),
                ], VisibleWhen: new("MA_NA_PUBLISHED", [yes])),
                new("lived_experience", "People with lived experience",
                [
                    new("MA_NA_LIVED_EXPERIENCE", "How did you engage with people with lived experience?", textarea,
                        Hint: "Describe the ways you engaged with them, including the number of people you spoke to and the communities you worked with"),
                ], VisibleWhen: new("MA_NA_PUBLISHED", [yes])),
                new("regional_hna", "Regional health needs assessment",
                [
                    new("MA_NA_REGIONAL", "Would you like there to be a regional health needs assessment?", radio, Options: RegionalHnaOptions),
                    Optional("MA_NA_REGIONAL_IMPROVE", "How could your regional health needs assessment be improved?", textarea,
                        hint: "Tell us about any changes that might make the regional health needs assessment more useful to your local authority"),
                ]),
            ]),

            new("strategic_approach", "Strategic approach",
            [
                new("steering_group", "Steering group",
                [
                    new("MA_SG_EXISTS", "Does your local authority have a multi-agency steering group for gambling harms prevention?", radio,
                        Options:
                        [
                            new(yes, "Yes"),
                            new(MaturityOptionValues.No, "No"),
                            new(MaturityOptionValues.InDevelopment, "In development"),
                        ],
                        PrefillSource: QuarterlyPrefill),
                ]),
                new("steering_group_members", "Steering group members",
                [
                    .. Matrix("MA_SG_MEMBERS", "Who is in the steering group?", StakeholderItems, SteeringGroupEngagement, QuarterlyPrefill),
                    Optional("MA_SG_MEMBERS_OTHER", "Who else is in the steering group?", textarea, prefillSource: QuarterlyPrefill),
                ], VisibleWhen: new("MA_SG_EXISTS", [yes])),
                new("action_plan", "Action plan",
                [
                    new("MA_ACTION_PLAN", "Does your local authority have a formalised action plan for gambling harms prevention, with timelines and responsible officers?", radio,
                        Options:
                        [
                            new(yes, "Yes"),
                            new(MaturityOptionValues.No, "No"),
                            new(MaturityOptionValues.InProgress, "In progress"),
                            new(other, "Other"),
                        ],
                        PrefillSource: QuarterlyPrefill),
                    OtherText("MA_ACTION_PLAN", "Tell us about your action plan"),
                ]),
                new("strategies", "Strategies",
                [
                    .. Matrix("MA_STRATEGIES", "Is gambling harms prevention part of your strategy on the following topics?", StrategyItems, StrategyOptions, QuarterlyPrefill),
                    Optional("MA_STRATEGIES_OTHER", "Is gambling harms prevention part of any other local strategies?", textarea,
                        hint: "List any other strategies that include gambling harms prevention"),
                ]),
                new("health_wellbeing_board", "Health and wellbeing board",
                [
                    new("MA_HWB_PRESENTED", "Has your team presented work on gambling harms to your local health and wellbeing board?", radio,
                        Options: YesNoOther, PrefillSource: QuarterlyPrefill),
                    OtherText("MA_HWB_PRESENTED", "Tell us about the work presented to your local health and wellbeing board"),
                    new("MA_HWB_LAST", "When did you last present to your local health and wellbeing board?", radio, Options: HwbYears,
                        ConditionalQuestionCode: "MA_HWB_PRESENTED", ConditionalValue: yes),
                ]),
                new("staff_support", "Staff support",
                [
                    new("MA_STAFF_SUPPORT", "Does your local authority have policies and support for staff affected by gambling harms, for example in a workplace charter?", radio,
                        Options:
                        [
                            new(yes, "Yes"),
                            new(MaturityOptionValues.No, "No"),
                            new(MaturityOptionValues.PlanningToExplore, "No - but we are planning to explore this"),
                            new(MaturityOptionValues.DontKnow, "Do not know"),
                        ],
                        PrefillSource: QuarterlyPrefill),
                ]),
            ]),

            new("awareness_training_signposting", "Awareness, training and signposting",
            [
                new("training", "Training",
                [
                    Optional("MA_TRAINING_NEEDS", "Would training on gambling harms prevention be useful for you or your team?", textarea,
                        hint: "Describe the sorts of training that might be helpful"),
                    .. Matrix("MA_TRAINED_WORKFORCES", "Has your public health team commissioned or promoted training on gambling harms for these workforces?", StakeholderItems, TrainedOptions, QuarterlyPrefill),
                    Optional("MA_TRAINED_OTHER", "Has your public health team commissioned or promoted training on gambling harms to any other workforces?", textarea,
                        hint: "List any other workforces who have received training", prefillSource: QuarterlyPrefill),
                ]),
                new("screening_tools", "Screening tools",
                [
                    .. Matrix("MA_SCREENING", "Do these service areas use a screening tool for referral to treatment services?", StakeholderItems, YesNoDontKnow, QuarterlyPrefill),
                    Optional("MA_SCREENING_OTHER", "Do any other service areas use a screening tool for gambling harms referrals?", textarea,
                        hint: "List any other service areas that use a screening tool", prefillSource: QuarterlyPrefill),
                ]),
                new("referral_pathways", "Referral pathways",
                [
                    new("MA_REFERRAL_PATHWAY", "Does your local authority have an established process for residents to access treatment and support services for gambling?", radio,
                        Hint: "For example through signposting or referrals",
                        Options:
                        [
                            new(yes, "Yes"),
                            new(MaturityOptionValues.No, "No"),
                            new(MaturityOptionValues.Partly, "Partly"),
                            new(MaturityOptionValues.DontKnow, "Don't know"),
                        ],
                        PrefillSource: QuarterlyPrefill),
                ]),
            ]),

            new("planning_licensing", "Planning and licensing",
            [
                new("licensing_policy_statement", "Licencing policy statement",
                [
                    new("MA_LIC_POLICY_URL", "What is the web address of your latest gambling licencing policy statement?", text),
                    new("MA_LIC_PH_INVOLVED", "Was the public health team involved in developing your latest gambling licencing policy statement?", radio,
                        Options: YesNoDontKnow, PrefillSource: QuarterlyPrefill),
                ]),
                new("local_area_profile", "Local area profile",
                [
                    new("MA_LIC_LOCAL_PROFILE", "Does your gambling licensing policy statement include a local area profile?", radio,
                        Options:
                        [
                            new(yes, "Yes"),
                            new(MaturityOptionValues.No, "No"),
                            new(MaturityOptionValues.DontKnow, "Don't know"),
                            new(MaturityOptionValues.LinksToJsna, "It includes a link to our JSNA/HNA"),
                        ],
                        PrefillSource: QuarterlyPrefill),
                    new("MA_LIC_PROFILE_FACTORS", "What public health factors are included in your local area profile?", textarea,
                        ConditionalQuestionCode: "MA_LIC_LOCAL_PROFILE", ConditionalValue: yes),
                    Optional("MA_LIC_OTHER_WAYS", "In what other ways did the public health team help develop your latest local gambling licencing policy statement?", textarea),
                ], VisibleWhen: new("MA_LIC_PH_INVOLVED", [yes])),
                new("gambling_licensing", "Gambling licensing",
                [
                    new("MA_LIC_RESTRICTED", "Has your team refused any gambling premises licenses or placed restrictions or additional conditions on them due to public health concerns?", radio,
                        Options: YesNoDontKnow, PrefillSource: QuarterlyPrefill),
                    new("MA_LIC_INVITED", "Is it standard practice for your public health team to be asked to submit representations for gambling licensing applications?", radio,
                        Options: YesNoDontKnow, PrefillSource: QuarterlyPrefill),
                ]),
                new("gambling_premises", "Gambling premises",
                [
                    new("MA_PLAN_GUIDANCE", "Are gambling harms or premises included in your local planning guidance?", radio,
                        Options: YesNoDontKnowOther, PrefillSource: QuarterlyPrefill),
                    OtherText("MA_PLAN_GUIDANCE", "Tell us how gambling harms or premises are covered in your local planning guidance"),
                    new("MA_PLAN_INVITED", "Is it standard practice for your public health team to be asked to submit representations for gambling planning applications?", radio,
                        Options: YesNoDontKnow, PrefillSource: QuarterlyPrefill),
                ]),
            ]),

            new("local_campaigns", "Local campaigns",
            [
                new("raising_awareness", "Raising awareness",
                [
                    new("MA_CAMPAIGN", "Have you delivered any local awareness campaigns on gambling harms in the past year?", radio,
                        Options:
                        [
                            new(yes, "Yes"),
                            new(MaturityOptionValues.No, "No"),
                            new(MaturityOptionValues.InProgress, "In progress"),
                            new(MaturityOptionValues.DontKnow, "Don't know"),
                            new(other, "Other"),
                        ],
                        PrefillSource: QuarterlyPrefill),
                    OtherText("MA_CAMPAIGN", "Tell us about your local awareness campaigns"),
                    new("MA_CAMPAIGN_AUDIENCE", "Who was the target audience for your campaign?", textarea,
                        Hint: "Describe or list the groups of people you wanted to reach with your campaign",
                        ConditionalQuestionCode: "MA_CAMPAIGN", ConditionalValue: campaignFollowUpAnswers, PrefillSource: QuarterlyPrefill),
                    new("MA_CAMPAIGN_AIMS", "What was the aim of your campaign?", checkbox, Hint: "Select all that apply", Options: CampaignAims,
                        ConditionalQuestionCode: "MA_CAMPAIGN", ConditionalValue: campaignFollowUpAnswers),
                    OtherText("MA_CAMPAIGN_AIMS", "What was the other aim of your campaign?"),
                    Optional("MA_CAMPAIGN_URL", "Can you share a web address linking to any resources developed as part of this campaign?", text,
                        conditionalQuestionCode: "MA_CAMPAIGN", conditionalValue: campaignFollowUpAnswers),
                ]),
            ]),

            new("advertising_restrictions", "Restriction of local advertising",
            [
                new("council_owned_spaces", "Council-owned advertising spaces",
                [
                    new("MA_ADV_RESTRICTED", "Has your local authority implemented local advertising restrictions for gambling on any council-owned advertising spaces?", radio,
                        Options:
                        [
                            new(yes, "Yes"),
                            new(MaturityOptionValues.No, "No"),
                            new(MaturityOptionValues.InProgress, "In progress"),
                            new(MaturityOptionValues.Considering, "No - but we are considering doing so"),
                            new(MaturityOptionValues.DontKnow, "Don't know"),
                        ],
                        PrefillSource: QuarterlyPrefill),
                    new("MA_ADV_DATE", "When did you introduce the restrictions?", text, Hint: "Please provide your best estimate in dd/mm/yyyy format.",
                        ConditionalQuestionCode: "MA_ADV_RESTRICTED", ConditionalValue: yes),
                    new("MA_ADV_TYPE", "What type of gambling advertising have you restricted?", radio,
                        Options:
                        [
                            new("all_gambling", "All types of gambling"),
                            new("all_excluding_lottery", "All gambling excluding lottery or lottery funded projects"),
                            new("online_gambling", "Online gambling"),
                            new(MaturityOptionValues.DontKnow, "Don't know"),
                            new(other, "Other"),
                        ],
                        ConditionalQuestionCode: "MA_ADV_RESTRICTED", ConditionalValue: yes),
                    OtherText("MA_ADV_TYPE", "What other type of gambling advertising have you restricted?"),
                    new("MA_ADV_ALL_PROPERTY", "Do the restrictions apply to all council-owned property?", radio, Options: YesNoDontKnowOther,
                        ConditionalQuestionCode: "MA_ADV_RESTRICTED", ConditionalValue: yes),
                    OtherText("MA_ADV_ALL_PROPERTY", "Tell us which council-owned property the restrictions apply to"),
                    new("MA_ADV_EXEMPT", "What council-owned property is exempt?", textarea,
                        ConditionalQuestionCode: "MA_ADV_ALL_PROPERTY", ConditionalValue: MaturityOptionValues.No),
                ]),
                new("other_restrictions", "Other advertising restrictions",
                [
                    new("MA_ADV_OTHER_COMMODITIES", "Have you restricted advertising for other commodities in your area, like unhealthy food, tobacco or alcohol?", radio,
                        Options:
                        [
                            new(yes, "Yes"),
                            new(MaturityOptionValues.No, "No"),
                            new(MaturityOptionValues.InProgress, "In progress"),
                            new(MaturityOptionValues.Considering, "No, but we are considering doing so"),
                        ],
                        PrefillSource: QuarterlyPrefill),
                ]),
            ]),

            new("working_with_vcse", "Working with VCSE",
            [
                new("commissioned_organisations", "Commissioned organisations",
                [
                    new("MA_VCSE_COMMISSIONING", "Are you currently commissioning any Voluntary, Community, and Social Enterprises (VCSEs) organisations to work on gambling harms prevention locally?", radio, Options: YesNoDontKnow),
                    new("MA_VCSE_COMMISSIONED", "Which organisations are you commissioning to work on gambling harms prevention?", checkbox, Hint: "Select all that apply",
                        Options: VcseOrganisations, ConditionalQuestionCode: "MA_VCSE_COMMISSIONING", ConditionalValue: yes),
                    OtherText("MA_VCSE_COMMISSIONED", "Which other organisations are you commissioning?"),
                    new("MA_VCSE_CONTRACTS", "What are the contract details for the VCSEs you are commissioning?", textarea,
                        Hint: "List the organisation name, start date, end date and value of the contract, like GamCare, 01/01/2023 to 31/12/2026, £10,000",
                        ConditionalQuestionCode: "MA_VCSE_COMMISSIONING", ConditionalValue: yes),
                    new("MA_VCSE_SERVICES", "What services do they deliver?", checkbox, Hint: "Select all that apply",
                        Options: VcseServices, ConditionalQuestionCode: "MA_VCSE_COMMISSIONING", ConditionalValue: yes),
                    OtherText("MA_VCSE_SERVICES", "What other services do they deliver?"),
                ]),
                new("non_commissioned_organisations", "Non-commissioned organisations",
                [
                    new("MA_VCSE_ENGAGED", "Are there any Voluntary, Community, and Social Enterprises (VCSEs) you work with on gambling harms prevention but do not commission?", radio, Options: YesNoDontKnow),
                    new("MA_VCSE_ENGAGED_NAMES", "Which organisations do you work with but do not commission?", checkbox, Hint: "Select all that apply",
                        Options: VcseOrganisations, ConditionalQuestionCode: "MA_VCSE_ENGAGED", ConditionalValue: yes),
                    OtherText("MA_VCSE_ENGAGED_NAMES", "Which other organisations do you work with?"),
                    new("MA_VCSE_ENGAGED_SERVICES", "What services do they deliver?", checkbox, Hint: "Select all that apply",
                        Options: VcseServices, ConditionalQuestionCode: "MA_VCSE_ENGAGED", ConditionalValue: yes),
                    OtherText("MA_VCSE_ENGAGED_SERVICES", "What other services do they deliver?"),
                ]),
            ]),

            new("other_preventative_services", "Other preventative services",
            [
                new("other_activities", "Other activities",
                [
                    new("MA_OTHER_SERVICES", "Do you provide any other gambling harms prevention services or activities funded or not funded by the levy?", radio,
                        Options:
                        [
                            new(yes, "Yes"),
                            new(MaturityOptionValues.No, "No"),
                            new(MaturityOptionValues.NoButPlanTo, "No but we plan to"),
                            new(MaturityOptionValues.DontKnow, "Don't know"),
                        ]),
                    new("MA_OTHER_SERVICES_DETAILS", "What other services or activities do you provide?", textarea,
                        ConditionalQuestionCode: "MA_OTHER_SERVICES", ConditionalValue: yes),
                ]),
            ]),

            new("next_steps", "Next steps for your LA",
            [
                new("next_steps", "Next steps",
                [
                    new("MA_PRIORITIES", "What are your biggest priorities when it comes to preventing gambling harms in your local authority?", textarea),
                ]),
            ]),
        ];
    }

    // One radio question per item, unconditional on the item (unlike the quarterly LSG_ENGAGEMENT_* block, whose
    // rows reveal per ticked member). The item name is the LABEL because check-your-answers rows, the CSV columns
    // and the SummaryList testids all key on it; GroupLabel carries the mapping-file question for the page heading.
    private static IEnumerable<MaturityQuestionSpec> Matrix(
        string codePrefix,
        string groupLabel,
        IReadOnlyList<ResolvedQuestionOption> items,
        IReadOnlyList<ResolvedQuestionOption> options,
        string? prefillSource = null) =>
        items.Select(item => new MaturityQuestionSpec(
            $"{codePrefix}__{item.Value.ToUpperInvariant()}",
            item.Label,
            MaturityFieldTypes.Radio,
            Options: options,
            GroupLabel: groupLabel,
            PrefillSource: prefillSource,
            ErrorMessage: $"Select an answer for {item.Label}"));

    // BLUM-7924: the WTE is asked once per ticked non-funded role, not once for the lot. A single question conditional
    // on every role renders one reveal — the renderer deliberately gives a child matching several checkbox options a
    // single owner, the first ticked one — so it only ever appeared under the first role. One question per role, each
    // revealed by that role alone, is the quarterly LSG_ENGAGEMENT_{MEMBER} pattern (BLUM-7788): the same
    // ConditionalQuestionCode/ConditionalValue pair every port of the visibility rule already handles, so no client
    // change. An unticked role is invisible, and therefore neither shown nor required nor exported.
    //
    // The role is in the LABEL, not only in the reveal it sits under, because check-your-answers rows, the CSV headers
    // and the SummaryList testids all key on the label — six rows reading the same question name no role and collide.
    // No GroupLabel: a conditional child joins its parent's block, so the entry and check pages would drop the heading
    // the read-only views and the CSV would still show.
    private static IEnumerable<MaturityQuestionSpec> NonFundedWte() =>
        RoleOptions.Select(role => new MaturityQuestionSpec(
            NonFundedWteCode(role.Value),
            $"What is the whole-time equivalent (WTE) for the roles not funded by the levy? ({role.Label})",
            MaturityFieldTypes.Radio,
            Hint: "Please provide your best estimate. 1 WTE = one person (equivalent) working full time on gambling harms prevention.",
            Options: WteBands,
            ConditionalQuestionCode: "MA_NONFUNDED_ROLES",
            ConditionalValue: role.Value,
            ErrorMessage: $"Select the whole-time equivalent (WTE) for {role.Label}"));

    // The "(optional)" marker lives in the label (see the class comment), so the one place a question is declared
    // optional is also the one place the copy says so.
    private static MaturityQuestionSpec Optional(
        string code,
        string label,
        string fieldType,
        string? hint = null,
        string? prefillSource = null,
        string? conditionalQuestionCode = null,
        string? conditionalValue = null) =>
        new(code, label + OptionalSuffix, fieldType, Hint: hint, IsRequired: false, PrefillSource: prefillSource,
            ConditionalQuestionCode: conditionalQuestionCode, ConditionalValue: conditionalValue);

    // The free-text companion every "Other" option reveals (BLUM-7472's rule: an "other" checkbox or radio with no
    // text companion cannot say what "other" means, and the choice validator refuses free text in the parent code).
    private static MaturityQuestionSpec OtherText(string parentCode, string label) =>
        new($"{parentCode}_OTHER", label, MaturityFieldTypes.Textarea,
            ConditionalQuestionCode: parentCode, ConditionalValue: MaturityOptionValues.Other);
}