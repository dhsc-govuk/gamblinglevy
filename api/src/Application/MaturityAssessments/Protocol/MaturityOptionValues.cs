namespace Application.MaturityAssessments.Protocol;

// The stored option VALUES the maturity assessment's seed, pre-fill rules, baseline mapping and validators all agree
// on. Labels live on the protocol; these are what maturity_assessment_answers and maturity_baseline_answers hold,
// so a rename here is a data migration, not a copy change. None may contain a comma: multi-select answers are
// comma-joined.
public static class MaturityOptionValues
{
    public const string Yes = "yes";
    public const string No = "no";
    public const string InProgress = "in_progress";
    public const string InDevelopment = "in_development";
    public const string DontKnow = "dont_know";
    public const string NotSure = "not_sure";
    public const string Other = "other";
    public const string Partly = "partly";
    public const string Maybe = "maybe";
    public const string Considering = "considering";
    public const string WouldLikeTo = "would_like_to";
    public const string PlanningToExplore = "planning_to_explore";
    public const string NoButPlanTo = "no_plan_to";
    public const string LinksToJsna = "links_to_jsna";
    public const string HaveAndUse = "have_use";
    public const string HaveDontUse = "have_dont_use";
    public const string NeedsUpdating = "needs_updating";
    public const string NoStrategy = "no_strategy";
    public const string NoDateSet = "no_date_set";
    public const string Pre2020 = "pre_2020";
    public const string Post2030 = "post_2030";
    public const string None = "none";
    public const string ContactedNoResponse = "contacted_no_response";

    // Steering-group engagement — the quarterly protocol's EngagementLevels values verbatim, so LSG_ENGAGEMENT_*
    // answers copy across untranslated.
    public const string AttendsEngages = "attends_engages";
    public const string AttendsLimited = "attends_limited";
    public const string NoEngagement = "no_engagement";
    public const string DoNotKnow = "do_not_know";
}