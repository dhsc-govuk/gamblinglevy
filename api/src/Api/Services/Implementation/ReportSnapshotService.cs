using Api.Dtos;
using Api.Services.Abstract;
using Api.Services.ReportSnapshot;
using Application.Common.Features;
using Application.Common.Permissions;
using Application.Common.ReportSnapshots;
using Application.Forms.Answers;
using Application.Submissions;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;

namespace Api.Services.Implementation;

// Builds the frozen report snapshot from the same authorised read side the screens use, applying the visibility filter
// (AnswerVisibility) and answer rendering (ReportAnswerPresenter) exactly once. The export then only formats the result, so there is a
// single source of truth for what the finalised report contains.
public class ReportSnapshotService(IQuarterlyReportControllerService reportService, IFeatureFlags featureFlags) : IReportSnapshotService
{
    // A row for spend against a no-longer-reportable activity carries actuals only, so every forecast cell reads
    // back null through GetValueOrDefault and renders blank.
    private static readonly IReadOnlyDictionary<int, decimal?> NoForecast = new Dictionary<int, decimal?>();

    public async Task<Option<ReportSnapshotDocument>> BuildAsync(Permission permission, Guid collectionId, CancellationToken cancellationToken)
    {
        // Only the sections this report actually has (BLUM-8106). A quarterly collection can be published with any
        // subset of the three modules, and the submission freezes that choice into its sections at start — the task
        // list the LA worked from. Rendering every module regardless put a deselected section into the frozen report
        // anyway: activity results pre-filled from the activity records, a funding row per activity, every blank
        // feedback page. The submission's sections, never the collection's CURRENT forms, because an admin can edit
        // an open window's forms after LAs have started (BLUM-8093) and that does not reshape their reports.
        var taskList = await reportService.GetTaskListAsync(permission, collectionId, cancellationToken);
        var moduleCodes = (taskList.IsNone ? [] : taskList.ValueUnsafe().Sections)
            .Select(s => s.ModuleCode)
            .ToHashSet(StringComparer.Ordinal);

        // BLUM_9087: a question whose parent is hidden is left out too.
        var transitive = featureFlags.IsEnabled(Feature.BLUM_9087);

        var activitySections = new List<ReportSnapshotSection>();
        if (moduleCodes.Contains(SubmissionModules.ActivityKeyResults))
        {
            var activities = (await reportService.GetReportableActivitiesAsync(permission, collectionId, cancellationToken))
                .Match(rows => rows, () => []);

            foreach (var activity in activities)
            {
                var form = await reportService.GetActivityQuestionsAsync(permission, collectionId, activity.ActivityId, cancellationToken);
                form.IfSome(f => activitySections.Add(BuildSection(
                    f.ActivityTitle,
                    f.Questions,
                    f.Answers.ToDictionary(a => a.QuestionCode, a => a.Value, StringComparer.Ordinal),
                    transitive)));
            }
        }

        // A report with no funding section to render snapshots as null funding, not as a failure. IsNone/ValueUnsafe
        // rather than Match: Option.Match runs LanguageExt's Check.NullReturn on the chosen branch, so the None arm
        // returning null threw ResultIsNullException (BLUM-7216). This one was reachable — the submit controller
        // builds the snapshot BEFORE the command re-checks eligibility, so an organisation de-assigned from the
        // collection mid-report got a 500 here instead of the command's clean "not eligible" response.
        ReportSnapshotFunding? funding = null;
        if (moduleCodes.Contains(SubmissionModules.FundingSpent))
        {
            var fundingLookup = await reportService.GetFundingSpendAsync(permission, collectionId, cancellationToken);
            funding = fundingLookup.IsNone ? null : BuildFunding(fundingLookup.ValueUnsafe());
        }

        var feedbackSections = moduleCodes.Contains(SubmissionModules.StrategicFeedback)
            ? (await reportService.GetStrategicFeedbackAsync(permission, collectionId, cancellationToken))
                .Match(feedback => BuildFeedbackSections(feedback, transitive), () => new List<ReportSnapshotSection>())
            : [];

        return new ReportSnapshotDocument(activitySections, funding, feedbackSections);
    }

    public string Serialize(ReportSnapshotDocument snapshot) => ReportSnapshotJson.Serialize(snapshot);

    public ReportSnapshotDocument? Deserialize(string json) => ReportSnapshotJson.Deserialize(json);

    // Only VISIBLE questions are emitted (conditional-visibility filter), each rendered through the shared presenter,
    // so conditionally-hidden questions (e.g. a prefilled "..._CONTRIBUTORS_OTHER" whose parent isn't "other") never
    // leak into the export the way the raw saved answers would.
    private static ReportSnapshotSection BuildSection(
        string title,
        IReadOnlyList<ReportQuestionDto> questions,
        IReadOnlyDictionary<string, string?> answers,
        bool transitive)
    {
        var visibility = AnswerVisibility.Of(
            questions.Select(q => VisibilityNode.For(q.Code, q.ConditionalQuestionCode, q.ConditionalValue)),
            answers,
            transitive);

        var rows = new List<ReportSnapshotRow>();
        foreach (var question in questions.OrderBy(q => q.DisplayOrder))
        {
            if (!visibility.IsVisible(question.Code))
            {
                continue;
            }

            answers.TryGetValue(question.Code, out var value);
            rows.Add(new ReportSnapshotRow(question.Label, ReportAnswerPresenter.RenderAnswer(question, value), question.Code));
        }

        return new ReportSnapshotSection(title, rows);
    }

    private static List<ReportSnapshotSection> BuildFeedbackSections(StrategicFeedbackDto feedback, bool transitive)
    {
        var answers = feedback.Answers.ToDictionary(a => a.QuestionCode, a => a.Value, StringComparer.Ordinal);
        return feedback.Pages
            .Select(page => BuildSection(page.Title, page.Questions, answers, transitive))
            .ToList();
    }

    private static ReportSnapshotFunding BuildFunding(FundingSpendDto funding)
    {
        var rows = new List<IReadOnlyList<string>>();
        foreach (var activity in funding.Activities)
        {
            rows.Add(BuildFundingRow(
                activity.Title,
                activity.CategoryName ?? string.Empty,
                activity.ActivityId.ToString(),
                activity.ActualByQuarter,
                activity.ForecastByQuarter,
                activity.TotalActualToDate,
                activity.ProjectedActivitySpend));
        }

        // Checked across the WHOLE year (not just this quarter's actual/remaining forecast) because the row this
        // gates now renders OtherActualByQuarter/OtherForecastByQuarter for every quarter — Other spend recorded
        // only in an earlier quarter (nothing this quarter, nothing forecast) must still produce a row, or the
        // org-level totals (which fold Other's historical quarters in unconditionally) stop reconciling against
        // what funding-spend.csv actually shows.
        var hasOther = funding.OtherActualByQuarter.Values.Any(v => v is not null)
            || funding.OtherForecastByQuarter.Values.Any(v => v is not null);
        if (hasOther)
        {
            rows.Add(BuildFundingRow(
                "Other (not linked to an activity)",
                string.Empty,
                string.Empty,
                funding.OtherActualByQuarter,
                funding.OtherForecastByQuarter,
                funding.OtherTotalActualToDate,
                funding.OtherProjectedSpend));
        }

        // Spend recorded against activities that no longer have a row of their own, appended last so the file is
        // byte-identical for the organisations that have none (BLUM-7607). Without these the per-activity table and
        // the organisation totals describe different amounts of money — an organisation whose ONLY spend is orphaned
        // used to contribute no row here at all while funding-summary.csv still showed a non-zero total. Deliberately
        // outside the hasOther branch: the two are independent.
        //
        // Actuals only: the forecast cells are blank (an empty map reads as null through GetValueOrDefault) and the
        // projection is the actual to date, matching FundingSpendUnreportedActivityDto's contract.
        foreach (var unreported in funding.UnreportedActivities)
        {
            rows.Add(BuildFundingRow(
                unreported.Title,
                unreported.CategoryName ?? string.Empty,
                unreported.ActivityId?.ToString() ?? string.Empty,
                unreported.ActualByQuarter,
                NoForecast,
                unreported.TotalActualToDate,
                unreported.TotalActualToDate));
        }

        var summary = new List<ReportSnapshotRow>
        {
            new("Total grant allocation", Money(funding.Summary.TotalGrantAllocation)),
            new("Total spend this quarter", Money(funding.Summary.TotalSpendThisQuarter)),
            new("Forecasted year-end", Money(funding.Summary.ExpectedSpendForYear)),
            new("Total spend to date", Money(funding.Summary.TotalSpendToDate)),
            new("Remaining allocation", Money(funding.Summary.RemainingBudget)),
            new("Total Forecast", Money(funding.Summary.TotalForecast)),
            new("Forecasted variance", Money(funding.Summary.ProjectedVariance))
        };

        var totals = new ReportSnapshotFundingTotals(
            Money(funding.Summary.TotalGrantAllocation),
            ActualQuarterTotal(funding, 1),
            ForecastQuarterTotal(funding, 2),
            ActualQuarterTotal(funding, 2),
            ForecastQuarterTotal(funding, 3),
            ActualQuarterTotal(funding, 3),
            ForecastQuarterTotal(funding, 4),
            ActualQuarterTotal(funding, 4),
            Money(funding.Summary.TotalSpendToDate),
            Money(funding.Summary.RemainingBudget),
            Money(funding.Summary.TotalForecast),
            Money(funding.Summary.ExpectedSpendForYear),
            Money(funding.Summary.ProjectedVariance),
            funding.Summary.GrantAllocationFinancialYear,
            funding.Summary.NextYearGrantAllocation is { } nextYearAllocation ? Money(nextYearAllocation) : null,
            funding.Summary.NextYearFinancialYear);

        return new ReportSnapshotFunding(ReportSnapshotCsv.FundingHeader, rows, summary, totals);
    }

    private static List<string> BuildFundingRow(
        string title,
        string categoryName,
        string activityId,
        IReadOnlyDictionary<int, decimal?> actualByQuarter,
        IReadOnlyDictionary<int, decimal?> forecastByQuarter,
        decimal totalActualToDate,
        decimal projectedSpend) =>
        [
            title,
            categoryName,
            activityId,
            Money(actualByQuarter.GetValueOrDefault(1)),
            Money(forecastByQuarter.GetValueOrDefault(2)),
            Money(actualByQuarter.GetValueOrDefault(2)),
            Money(forecastByQuarter.GetValueOrDefault(3)),
            Money(actualByQuarter.GetValueOrDefault(3)),
            Money(forecastByQuarter.GetValueOrDefault(4)),
            Money(actualByQuarter.GetValueOrDefault(4)),
            Money(totalActualToDate),
            Money(projectedSpend)
        ];

    // Org-level Q{n} actual, read straight off the one figure the read side derives for it. It must NOT be
    // re-summed from the activity rows here: those cover only currently-reportable activities, while Total spend to
    // date covers every recorded entry, so the columns stopped adding up to the total as soon as an activity was
    // deleted or reverted to Planned (BLUM-7607). Blank (not zero) for a future quarter or a quarter with no entry at
    // all — "blank" means "not yet knowable", never "confirmed zero" (BLUM-7130 CSV review) — which Money(null)
    // already gives us. The quarter guard is belt-and-braces: a future quarter is already null in the map.
    private static string ActualQuarterTotal(FundingSpendDto funding, int quarter) =>
        quarter > funding.CurrentQuarter
            ? string.Empty
            : Money(funding.TotalActualByQuarter.GetValueOrDefault(quarter));

    // Org-level Q{n} forecast = the sum across every activity + "Other" for that quarter, blank when nothing was
    // forecast for it. Q1 has no value by construction (nothing precedes Q1 in the financial year to have forecast
    // it). Unlike the actual above this stays row-derived: a forecast against an activity with no row is absent from
    // BOTH this column and Summary.TotalForecast, so the two still agree. A Planned activity's forecast-only row
    // (BLUM-7672) is a row like any other, so its forecast is in both.
    private static string ForecastQuarterTotal(FundingSpendDto funding, int quarter)
    {
        var activityValues = funding.Activities.Select(a => a.ForecastByQuarter).ToList();
        var otherValue = funding.OtherForecastByQuarter;

        var hasAnyValue = activityValues.Any(d => d.TryGetValue(quarter, out var v) && v is not null)
            || (otherValue.TryGetValue(quarter, out var otherV) && otherV is not null);
        if (!hasAnyValue)
        {
            return string.Empty;
        }

        var total = activityValues.Sum(d => d.TryGetValue(quarter, out var v) ? v ?? 0m : 0m)
            + (otherValue.TryGetValue(quarter, out var ov) ? ov ?? 0m : 0m);
        return Money(total);
    }

    // Blank vs 0.00, and why negatives are parenthesised rather than signed, both live on ReportSnapshotMoney —
    // the export reads these cells back out of the frozen snapshot to total them per organisation, so the renderer
    // and the parser have to be one pair.
    private static string Money(decimal? amount) => ReportSnapshotMoney.Format(amount);
}