using Application.Common.Validation;
using Application.MaturityAssessments.Protocol;
using Domain.Activities;
using Domain.Categories;

namespace Application.MaturityAssessments.Prefill;

// The GREEN rows of "MA & QP data mapping 08092026.xlsx" as code: how the organisation's activity records and its
// latest submitted quarterly-report answers become maturity-assessment answers. One method per question family so
// each rule is unit-testable on its own; Build() merges them.
//
// Vocabulary (column P of the sheet): AnyYes — any contributing report says yes ⇒ yes, else every one says no ⇒ no,
// else nothing; AllYes — every one says yes ⇒ yes, any no ⇒ no; MostRecent — the activity whose latest report has
// the latest reporting-period end (then the newest record) wins; union — each member once. A rule that finds
// nothing writes nothing — a blank never overwrites a lower-precedence source.
//
// Status scopes are facts of KeyResultProtocolSeedData and constrain what can exist: RNW_*, LSG_*, LRA_*,
// PLA_INVITATIONS and ADV_* are only ever asked of an Active activity; LNA_PUBLISHED, TRN_SERVICE_AREAS,
// SCR_SERVICES and SAP_OTHER_STRATEGIES only of a Closed/Finished one; LNA_PUBLISHED_URL only of a Finished one.
public static class QuarterlyPrefillRules
{
    public static class TaskNames
    {
        public const string RegionalNetwork = "Joining and attending a regional network";
        public const string LocalNeedsAssessment = "Creating a local needs assessment";
        public const string SteeringGroup = "Setting up and running a local steering group";
        public const string StrategyAndActionPlan = "Documenting a strategy and action plan";
        public const string WorkplaceCharter = "Creating a workplace charter";
        public const string Training = "Training your workforce on preventing gambling harms";
        public const string ScreeningTools = "Using screening tools to better assess gambling harms";
        public const string Referrals = "Improving referrals between services";
        public const string LicensingPolicy = "Reviewing and evolving current licensing policy";
        public const string LicenceApplications = "Responding to gambling licence applications";
        public const string PlanningApplications = "Managing planning applications for gambling venues";
        public const string Campaigns = "Running campaigns and workshops to raise awareness among local residents and at-risk groups";
        public const string Advertising = "Restricting gambling advertising on council-owned spaces";
        public const string OtherRole = "Other role";

        public static readonly IReadOnlyList<string> ProtocolTypes =
        [
            RegionalNetwork, LocalNeedsAssessment, SteeringGroup, StrategyAndActionPlan, WorkplaceCharter, Training,
            ScreeningTools, Referrals, LicensingPolicy, LicenceApplications, PlanningApplications, Campaigns, Advertising,
        ];
    }

    private const string Yes = MaturityOptionValues.Yes;
    private const string No = MaturityOptionValues.No;

    public static IReadOnlyDictionary<string, string> Build(QuarterlyPrefillInput input)
    {
        var output = new Dictionary<string, string>(StringComparer.Ordinal);

        FundedRoles(input, output);
        StrategicNetwork(input, output);
        NeedsAssessment(input, output);
        SteeringGroup(input, output);
        StrategyAndActionPlan(input, output);
        StaffSupport(input, output);
        Training(input, output);
        Screening(input, output);
        ReferralPathway(input, output);
        LicensingPolicy(input, output);
        LicenceApplications(input, output);
        Planning(input, output);
        Campaign(input, output);
        Advertising(input, output);

        return output;
    }

    // Sheet rows 10 / 10.5 / 11: every current People-and-roles activity is one repeater row (duplicates kept —
    // "treat each activity as a unique activity even if the same role has been created twice"); "Other role"
    // records also fill the free-text other-roles question.
    public static void FundedRoles(QuarterlyPrefillInput input, Dictionary<string, string> output)
    {
        var roles = input.Activities
            .Where(a => a.CategoryName == CategoryNames.PeopleAndRoles && a.Status.IsActive)
            .OrderBy(a => a.CreatedAt)
            .ToList();

        if (roles.Count == 0)
        {
            return;
        }

        // "None" is the LA's own answer, never a quarterly one (BLUM-9112): no active role does not say no role is
        // funded, and a task that happens to be named "None" is an "Other role" like any unknown name.
        var roleByTaskName = MaturityProtocol.FundedRoleOptions
            .Where(o => o.Value != MaturityOptionValues.None)
            .ToDictionary(o => o.Label, o => o.Value, StringComparer.OrdinalIgnoreCase);
        var entries = roles
            .Select(a =>
            {
                var isOther = a.TaskName == TaskNames.OtherRole || !roleByTaskName.ContainsKey(a.TaskName);
                var role = isOther ? MaturityOptionValues.Other : roleByTaskName[a.TaskName];
                var time = a.RecordAnswers.TryGetValue("PR_FUNDING_TIME", out var t) ? t : string.Empty;
                return new MaturityRolesAnswer.Entry(role, time, isOther ? (a.CustomTaskText ?? a.TaskName) : null);
            })
            .Where(e => MaturityProtocol.FundedTimeOptions.Any(o => o.Value == e.Time))
            .Take(MaturityRolesAnswer.MaxRoles)
            .ToList();

        if (entries.Count > 0)
        {
            output["MA_FUNDED_ROLES"] = MaturityRolesAnswer.Serialize(entries);
        }

        var otherRoles = roles
            .Where(a => a.TaskName == TaskNames.OtherRole && !string.IsNullOrWhiteSpace(a.CustomTaskText))
            .Select(a => a.CustomTaskText!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (otherRoles.Count > 0)
        {
            output["MA_OTHER_ROLES"] = Truncate(string.Join("; ", otherRoles));
        }
    }

    // Sheet row 13: RNW_MEMBERSHIP over current regional-network activities, AnyYes (an ended network is ignored).
    public static void StrategicNetwork(QuarterlyPrefillInput input, Dictionary<string, string> output)
    {
        var answers = Reports(input, TaskNames.RegionalNetwork, a => a.Status.IsActive)
            .Select(r => r.Answers.GetValueOrDefault("RNW_MEMBERSHIP"));
        Set(output, "MA_STRATEGIC_NETWORK", AnyYes(answers));
    }

    // Sheet rows 16 / 17 / 18 / 29 / 40: the LOCAL needs assessment only (RNA_* never contributes). The published
    // assessment is the most recent Closed/Finished one whose report says LNA_PUBLISHED = yes — a newer draft never
    // replaces an older published assessment. Its web address, coverage and contributors come from that same
    // assessment; with no published one, the contributor union is taken across every active one.
    public static void NeedsAssessment(QuarterlyPrefillInput input, Dictionary<string, string> output)
    {
        var all = WithReports(input, TaskNames.LocalNeedsAssessment).ToList();
        if (all.Count == 0)
        {
            return;
        }

        var published = all
            .Where(x => x.Activity.Status.IsReadOnly && x.Report.Answers.GetValueOrDefault("LNA_PUBLISHED") == Yes)
            .OrderByDescending(x => x.Report.ReportingPeriodEnd)
            .ThenByDescending(x => x.Activity.CreatedAt)
            .FirstOrDefault();

        var inProgress = input.Activities.Any(a =>
            a.TaskName == TaskNames.LocalNeedsAssessment && a.Status.IsActive && a.KeyResultCodes.Contains("lna_published"));

        var closedSayNo = all.Where(x => x.Activity.Status.IsReadOnly).Select(x => x.Report.Answers.GetValueOrDefault("LNA_PUBLISHED")).ToList();

        IEnumerable<ActivityReport> contributorSources;
        if (published is not null)
        {
            output["MA_NA_PUBLISHED"] = Yes;
            Set(output, "MA_NA_URL", Truncate(published.Report.Answers.GetValueOrDefault("LNA_PUBLISHED_URL")));

            var coverage = Split(published.Report.Answers.GetValueOrDefault("LNA_INFORMATION"))
                .Where(v => MaturityProtocol.NeedsAssessmentCoverage.Any(o => o.Value == v))
                .ToList();
            if (coverage.Count > 0)
            {
                output["MA_NA_COVERAGE"] = string.Join(",", coverage);
            }

            contributorSources = [published];
        }
        else if (inProgress)
        {
            output["MA_NA_PUBLISHED"] = MaturityOptionValues.InProgress;
            contributorSources = all.Where(x => x.Activity.Status.IsActive);
        }
        else if (closedSayNo.Count > 0 && closedSayNo.All(v => v == No))
        {
            output["MA_NA_PUBLISHED"] = No;
            contributorSources = [];
        }
        else
        {
            contributorSources = [];
        }

        var contributors = contributorSources.ToList();
        if (contributors.Count == 0)
        {
            return;
        }

        var reports = contributors.Select(x => x.Report).ToList();
        SelectedMatrix(output, "MA_NA_ENGAGEMENT", reports, "LNA_CONTRIBUTORS");
        OtherText(output, "MA_NA_ENGAGEMENT_OTHER", reports, "LNA_CONTRIBUTORS");
    }

    // Sheet rows 44 / 45 / 58: a Finished steering-group activity ⇒ Yes, else an Active one ⇒ In development (a
    // Closed historic group is not evidence of a current one). Members come from the Active activities' LSG_MEMBERS
    // and their per-member LSG_ENGAGEMENT_* answers; only "attends and engages" / "attends but limited" are carried
    // (contacted-but-no-response and no-engagement are left for the LA), the better level winning across activities.
    // "Other" follows the same rule (BLUM-9247), and its free text is taken only from the reports whose Other is
    // carried: an Other that never attended is not a member, and the text must never show without its level.
    public static void SteeringGroup(QuarterlyPrefillInput input, Dictionary<string, string> output)
    {
        var groups = input.Activities.Where(a => a.TaskName == TaskNames.SteeringGroup).ToList();
        if (groups.Count == 0)
        {
            return;
        }

        if (groups.Any(a => a.Status.Equals(ActivityStatus.Finished)))
        {
            output["MA_SG_EXISTS"] = Yes;
        }
        else if (groups.Any(a => a.Status.IsActive))
        {
            output["MA_SG_EXISTS"] = MaturityOptionValues.InDevelopment;
        }

        var reports = Reports(input, TaskNames.SteeringGroup, a => a.Status.IsActive).ToList();
        if (reports.Count == 0)
        {
            return;
        }

        foreach (var item in MaturityProtocol.StakeholderItems)
        {
            var levels = reports
                .Select(r => SteeringGroupLevel(r, item.Value))
                .ToList();

            if (levels.Contains(MaturityOptionValues.AttendsEngages))
            {
                output[MatrixCode("MA_SG_MEMBERS", item.Value)] = MaturityOptionValues.AttendsEngages;
            }
            else if (levels.Contains(MaturityOptionValues.AttendsLimited))
            {
                output[MatrixCode("MA_SG_MEMBERS", item.Value)] = MaturityOptionValues.AttendsLimited;
            }
        }

        var otherAttends = reports
            .Where(r => SteeringGroupLevel(r, MaturityOptionValues.Other) is MaturityOptionValues.AttendsEngages or MaturityOptionValues.AttendsLimited)
            .ToList();
        OtherText(output, "MA_SG_MEMBERS_OTHER", otherAttends, "LSG_MEMBERS");
    }

    // Sheet rows 59 / 60 / 65: SAP_PLAN_DEVELOPED AnyYes across every status (worked example: an adopted plan plus a
    // refresh in progress ⇒ Yes); SAP_OTHER_STRATEGIES from Closed/Finished activities (checked ⇒ yes, unchecked ⇒
    // no, only when at least one such activity answered); SAP_HWB_PRESENTED AnyYes from Closed/Finished only —
    // the Active phrasing asks "will you present", which is not a presentation.
    public static void StrategyAndActionPlan(QuarterlyPrefillInput input, Dictionary<string, string> output)
    {
        var reports = Reports(input, TaskNames.StrategyAndActionPlan, _ => true).ToList();
        Set(output, "MA_ACTION_PLAN", AnyYes(reports.Select(r => r.Answers.GetValueOrDefault("SAP_PLAN_DEVELOPED"))));

        var closed = WithReports(input, TaskNames.StrategyAndActionPlan)
            .Where(x => x.Activity.Status.IsReadOnly)
            .Select(x => x.Report)
            .ToList();

        var strategyReports = closed.Where(r => r.Answers.ContainsKey("SAP_OTHER_STRATEGIES")).ToList();
        if (strategyReports.Count > 0)
        {
            var checkedValues = strategyReports
                .SelectMany(r => Split(r.Answers.GetValueOrDefault("SAP_OTHER_STRATEGIES")))
                .ToHashSet(StringComparer.Ordinal);

            foreach (var item in MaturityProtocol.StrategyItems)
            {
                output[MatrixCode("MA_STRATEGIES", item.Value)] = checkedValues.Contains(item.Value) ? Yes : No;
            }
        }

        Set(output, "MA_HWB_PRESENTED", AnyYes(closed.Select(r => r.Answers.GetValueOrDefault("SAP_HWB_PRESENTED"))));
    }

    // Sheet row 67: WPC_POLICIES from Closed/Finished workplace-charter activities, AnyYes — "the fact that an
    // activity is marked finished is not enough unless the result confirms that support is in place".
    public static void StaffSupport(QuarterlyPrefillInput input, Dictionary<string, string> output)
    {
        var answers = Reports(input, TaskNames.WorkplaceCharter, a => a.Status.IsReadOnly)
            .Select(r => r.Answers.GetValueOrDefault("WPC_POLICIES"));
        Set(output, "MA_STAFF_SUPPORT", AnyYes(answers));
    }

    // Sheet rows 69 / 81: TRN_SERVICE_AREAS union over Closed/Finished training activities (selected ⇒ yes,
    // unselected ⇒ no). "In progress" is not derivable — an Active training activity carries no per-workforce
    // question — so it is left for the LA.
    public static void Training(QuarterlyPrefillInput input, Dictionary<string, string> output)
    {
        var reports = Reports(input, TaskNames.Training, a => a.Status.IsReadOnly)
            .Where(r => r.Answers.ContainsKey("TRN_SERVICE_AREAS"))
            .ToList();
        if (reports.Count == 0)
        {
            return;
        }

        SelectedMatrix(output, "MA_TRAINED_WORKFORCES", reports, "TRN_SERVICE_AREAS");
        OtherText(output, "MA_TRAINED_OTHER", reports, "TRN_SERVICE_AREAS");
    }

    // Sheet rows 82 / 91: SCR_SERVICES union over Closed/Finished screening activities, each service once.
    public static void Screening(QuarterlyPrefillInput input, Dictionary<string, string> output)
    {
        var reports = Reports(input, TaskNames.ScreeningTools, a => a.Status.IsReadOnly)
            .Where(r => r.Answers.ContainsKey("SCR_SERVICES"))
            .ToList();
        if (reports.Count == 0)
        {
            return;
        }

        SelectedMatrix(output, "MA_SCREENING", reports, "SCR_SERVICES");
        OtherText(output, "MA_SCREENING_OTHER", reports, "SCR_SERVICES");
    }

    // Sheet row 92: REF_PATHWAYS AnyYes across every referral activity that has been reported on.
    public static void ReferralPathway(QuarterlyPrefillInput input, Dictionary<string, string> output)
    {
        var answers = Reports(input, TaskNames.Referrals, _ => true).Select(r => r.Answers.GetValueOrDefault("REF_PATHWAYS"));
        Set(output, "MA_REFERRAL_PATHWAY", AnyYes(answers));
    }

    // Sheet rows 94 / 95: LCP_PH_INVOLVEMENT AnyYes across every licensing-policy activity (policy cycles are not
    // identifiable, so "latest cycle only" cannot be applied); LCP_AREA_PROFILE from the most recent Closed/Finished
    // one (the latest PUBLISHED policy — an Active one is a draft and ignored).
    public static void LicensingPolicy(QuarterlyPrefillInput input, Dictionary<string, string> output)
    {
        var all = WithReports(input, TaskNames.LicensingPolicy).ToList();
        Set(output, "MA_LIC_PH_INVOLVED", AnyYes(all.Select(x => x.Report.Answers.GetValueOrDefault("LCP_PH_INVOLVEMENT"))));

        var latestPublished = all
            .Where(x => x.Activity.Status.IsReadOnly && x.Report.Answers.ContainsKey("LCP_AREA_PROFILE"))
            .OrderByDescending(x => x.Report.ReportingPeriodEnd)
            .ThenByDescending(x => x.Activity.CreatedAt)
            .FirstOrDefault();
        Set(output, "MA_LIC_LOCAL_PROFILE", YesNo(latestPublished?.Report.Answers.GetValueOrDefault("LCP_AREA_PROFILE")));
    }

    // Sheet rows 98 / 99: LRA_REFUSED AnyYes; LRA_INVITED AllYes ("invited on every eligible application").
    public static void LicenceApplications(QuarterlyPrefillInput input, Dictionary<string, string> output)
    {
        var reports = Reports(input, TaskNames.LicenceApplications, a => a.Status.IsActive).ToList();
        Set(output, "MA_LIC_RESTRICTED", AnyYes(reports.Select(r => r.Answers.GetValueOrDefault("LRA_REFUSED"))));
        Set(output, "MA_LIC_INVITED", AllYes(reports.Select(r => r.Answers.GetValueOrDefault("LRA_INVITED"))));
    }

    // Sheet rows 100 / 101: PLA_SPD from the most recent planning activity (the latest published guidance wins over
    // an older one); PLA_INVITATIONS AllYes.
    public static void Planning(QuarterlyPrefillInput input, Dictionary<string, string> output)
    {
        var all = WithReports(input, TaskNames.PlanningApplications).ToList();

        var latest = all
            .Where(x => x.Report.Answers.ContainsKey("PLA_SPD"))
            .OrderByDescending(x => x.Report.ReportingPeriodEnd)
            .ThenByDescending(x => x.Activity.CreatedAt)
            .FirstOrDefault();
        Set(output, "MA_PLAN_GUIDANCE", YesNo(latest?.Report.Answers.GetValueOrDefault("PLA_SPD")));

        var invited = all.Where(x => x.Activity.Status.IsActive).Select(x => x.Report.Answers.GetValueOrDefault("PLA_INVITATIONS"));
        Set(output, "MA_PLAN_INVITED", AllYes(invited));
    }

    // Sheet rows 102 / 103: only reports within the 12 months before the assessment period end count. A
    // Closed/Finished campaign that went live ⇒ Yes; else an Active one measuring cam_live ⇒ In progress; else every
    // Closed/Finished one says no ⇒ No. The audience is the union of the contributing activities' "Who was the
    // activity for" record answers, rendered as labels.
    public static void Campaign(QuarterlyPrefillInput input, Dictionary<string, string> output)
    {
        var windowStart = input.AssessmentPeriodEnd.AddMonths(-12);
        var all = WithReports(input, TaskNames.Campaigns)
            .Where(x => x.Report.ReportingPeriodEnd >= windowStart)
            .ToList();
        var active = input.Activities.Where(a => a.TaskName == TaskNames.Campaigns && a.Status.IsActive).ToList();

        var delivered = all.Where(x => x.Activity.Status.IsReadOnly && x.Report.Answers.GetValueOrDefault("CAM_LIVE") == Yes).ToList();
        var contributing = new List<ActivityFacts>();

        if (delivered.Count > 0)
        {
            output["MA_CAMPAIGN"] = Yes;
            contributing.AddRange(delivered.Select(x => x.Activity));
        }
        else if (active.Any(a => a.KeyResultCodes.Contains("cam_live")))
        {
            output["MA_CAMPAIGN"] = MaturityOptionValues.InProgress;
            contributing.AddRange(active);
        }
        else
        {
            var closed = all.Where(x => x.Activity.Status.IsReadOnly).Select(x => x.Report.Answers.GetValueOrDefault("CAM_LIVE")).ToList();
            if (closed.Count > 0 && closed.All(v => v == No))
            {
                output["MA_CAMPAIGN"] = No;
            }

            return;
        }

        var audience = new List<string>();
        foreach (var activity in contributing)
        {
            var whoFor = activity.RecordAnswers.FirstOrDefault(kv => kv.Key.EndsWith("_WHO_FOR", StringComparison.Ordinal)).Value;
            foreach (var value in Split(whoFor))
            {
                if (value == MaturityOptionValues.Other)
                {
                    var otherText = activity.RecordAnswers.FirstOrDefault(kv => kv.Key.EndsWith("_WHO_FOR_OTHER", StringComparison.Ordinal)).Value;
                    if (!string.IsNullOrWhiteSpace(otherText))
                    {
                        audience.Add(otherText.Trim());
                    }

                    continue;
                }

                audience.Add(input.WhoForLabels.TryGetValue(value, out var label) ? label : value);
            }
        }

        var distinct = audience.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (distinct.Count > 0)
        {
            output["MA_CAMPAIGN_AUDIENCE"] = Truncate(string.Join(", ", distinct));
        }
    }

    // Sheet rows 106 / 111: ADV_RESTRICTIONS yes_all/yes_some ⇒ Yes, every one no ⇒ No (In progress / considering
    // have no quarterly source); ADV_OTHER_RESTRICTIONS AnyYes. Active-only questions.
    public static void Advertising(QuarterlyPrefillInput input, Dictionary<string, string> output)
    {
        var reports = Reports(input, TaskNames.Advertising, a => a.Status.IsActive).ToList();

        var restrictions = reports.Select(r => r.Answers.GetValueOrDefault("ADV_RESTRICTIONS")).Where(v => v is not null).ToList();
        if (restrictions.Any(v => v is "yes_all" or "yes_some"))
        {
            output["MA_ADV_RESTRICTED"] = Yes;
        }
        else if (restrictions.Count > 0 && restrictions.All(v => v == No))
        {
            output["MA_ADV_RESTRICTED"] = No;
        }

        Set(output, "MA_ADV_OTHER_COMMODITIES", AnyYes(reports.Select(r => r.Answers.GetValueOrDefault("ADV_OTHER_RESTRICTIONS"))));
    }

    // ---- helpers -----------------------------------------------------------------------------------------

    public static string MatrixCode(string prefix, string itemValue) => $"{prefix}__{itemValue.ToUpperInvariant()}";

    // A reference type rather than a tuple so FirstOrDefault() yields null (a default tuple is indistinguishable
    // from a real one).
    private sealed record ActivityReport(ActivityFacts Activity, LatestReport Report);

    private static IEnumerable<ActivityReport> WithReports(QuarterlyPrefillInput input, string taskName) =>
        input.Activities
            .Where(a => a.TaskName == taskName && input.LatestReports.ContainsKey(a.ActivityId))
            .Select(a => new ActivityReport(a, input.LatestReports[a.ActivityId]));

    private static IEnumerable<LatestReport> Reports(QuarterlyPrefillInput input, string taskName, Func<ActivityFacts, bool> statusFilter) =>
        WithReports(input, taskName).Where(x => statusFilter(x.Activity)).Select(x => x.Report);

    // A member's LSG_ENGAGEMENT_* answer, or null when the report does not list them in LSG_MEMBERS.
    private static string? SteeringGroupLevel(LatestReport report, string member) =>
        Split(report.Answers.GetValueOrDefault("LSG_MEMBERS")).Contains(member)
            ? report.Answers.GetValueOrDefault($"LSG_ENGAGEMENT_{member.ToUpperInvariant()}")
            : null;

    // A stakeholder list ticked ⇒ yes, unticked ⇒ no. "Other" is answered only when ticked (BLUM-9247): its free text
    // merges per code, so a "no" here would sit beside text a prior assessment or the 2025 baseline (which maps the
    // text with no radio) still supplies, and on a refresh would overwrite a prior assessment's "yes".
    private static void SelectedMatrix(Dictionary<string, string> output, string prefix, IReadOnlyList<LatestReport> reports, string listCode)
    {
        var selected = reports.SelectMany(r => Split(r.Answers.GetValueOrDefault(listCode))).ToHashSet(StringComparer.Ordinal);
        foreach (var item in MaturityProtocol.StakeholderItems)
        {
            if (selected.Contains(item.Value))
            {
                output[MatrixCode(prefix, item.Value)] = Yes;
            }
            else if (item.Value != MaturityOptionValues.Other)
            {
                output[MatrixCode(prefix, item.Value)] = No;
            }
        }
    }

    // The {listCode}_OTHER free text, only from reports that ticked "other" in the list, so it never arrives without
    // the Other row being answered from the same reports. A quarterly save already drops the text of an unticked
    // Other; this guards rows that reached the database another way.
    private static void OtherText(Dictionary<string, string> output, string code, IReadOnlyList<LatestReport> reports, string listCode)
    {
        var others = reports
            .Where(r => Split(r.Answers.GetValueOrDefault(listCode)).Contains(MaturityOptionValues.Other))
            .Select(r => r.Answers.GetValueOrDefault($"{listCode}_OTHER"))
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (others.Count > 0)
        {
            output[code] = Truncate(string.Join("; ", others));
        }
    }

    private static string? AnyYes(IEnumerable<string?> answers)
    {
        var values = answers.Where(v => v is Yes or No).ToList();
        if (values.Count == 0)
        {
            return null;
        }

        return values.Contains(Yes) ? Yes : No;
    }

    private static string? AllYes(IEnumerable<string?> answers)
    {
        var values = answers.Where(v => v is Yes or No).ToList();
        if (values.Count == 0)
        {
            return null;
        }

        return values.All(v => v == Yes) ? Yes : No;
    }

    private static string? YesNo(string? value) => value is Yes or No ? value : null;

    private static void Set(Dictionary<string, string> output, string code, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            output[code] = value;
        }
    }

    private static string Truncate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        // Maturity free text has no character limit (BLUM-8411), but a joined value longer than the request ceiling
        // would be refused by SaveMaturityAnswersCommandValidator and leave the LA unable to save the page.
        var trimmed = value.Trim();
        return trimmed.Length <= FieldLimits.AnswerText ? trimmed : trimmed[..FieldLimits.AnswerText];
    }

    private static string[] Split(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}