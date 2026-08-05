using System.Globalization;
using Api.Dtos;
using Api.Services.Abstract;
using Api.Services.ReportSnapshot;
using Application.Common.Permissions;
using Application.Common.ReportSnapshots;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;

namespace Api.Services.Implementation;

// Builds the frozen report snapshot from the same authorised read side the screens use, applying the visibility filter
// and answer rendering (ReportAnswerPresenter) exactly once. The export then only formats the result, so there is a
// single source of truth for what the finalised report contains.
public class ReportSnapshotService(IQuarterlyReportControllerService reportService) : IReportSnapshotService
{
    public async Task<Option<ReportSnapshotDocument>> BuildAsync(Permission permission, Guid collectionId, CancellationToken cancellationToken)
    {
        var activities = (await reportService.GetReportableActivitiesAsync(permission, collectionId, cancellationToken))
            .Match(rows => rows, () => []);

        var activitySections = new List<ReportSnapshotSection>();
        foreach (var activity in activities)
        {
            var form = await reportService.GetActivityQuestionsAsync(permission, collectionId, activity.ActivityId, cancellationToken);
            form.IfSome(f => activitySections.Add(BuildSection(
                f.ActivityTitle,
                f.Questions,
                f.Answers.ToDictionary(a => a.QuestionCode, a => a.Value, StringComparer.Ordinal))));
        }

        // A report with no funding section to render snapshots as null funding, not as a failure. IsNone/ValueUnsafe
        // rather than Match: Option.Match runs LanguageExt's Check.NullReturn on the chosen branch, so the None arm
        // returning null threw ResultIsNullException (BLUM-7216). This one was reachable — the submit controller
        // builds the snapshot BEFORE the command re-checks eligibility, so an organisation de-assigned from the
        // collection mid-report got a 500 here instead of the command's clean "not eligible" response.
        var fundingLookup = await reportService.GetFundingSpendAsync(permission, collectionId, cancellationToken);
        var funding = fundingLookup.IsNone ? null : BuildFunding(fundingLookup.ValueUnsafe());

        var feedbackSections = (await reportService.GetStrategicFeedbackAsync(permission, collectionId, cancellationToken))
            .Match(BuildFeedbackSections, () => new List<ReportSnapshotSection>());

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
        IReadOnlyDictionary<string, string?> answers)
    {
        var rows = new List<ReportSnapshotRow>();
        foreach (var question in questions.OrderBy(q => q.DisplayOrder))
        {
            if (!ReportAnswerPresenter.IsVisible(question, answers))
            {
                continue;
            }

            answers.TryGetValue(question.Code, out var value);
            rows.Add(new ReportSnapshotRow(question.Label, ReportAnswerPresenter.RenderAnswer(question, value)));
        }

        return new ReportSnapshotSection(title, rows);
    }

    private static List<ReportSnapshotSection> BuildFeedbackSections(StrategicFeedbackDto feedback)
    {
        var answers = feedback.Answers.ToDictionary(a => a.QuestionCode, a => a.Value, StringComparer.Ordinal);
        return feedback.Pages
            .Select(page => BuildSection(page.Title, page.Questions, answers))
            .ToList();
    }

    private static ReportSnapshotFunding BuildFunding(FundingSpendDto funding)
    {
        var quarters = funding.RemainingQuarters.Select(q => q.Quarter).ToList();

        var header = new List<string> { "Activity", "Levy spent this quarter (£)", "Total spent so far (£)" };
        header.AddRange(quarters.Select(q => $"Forecast Q{q} (£)"));

        var rows = new List<IReadOnlyList<string>>();
        foreach (var activity in funding.Activities)
        {
            var row = new List<string>
            {
                activity.Title,
                Money(activity.ActualThisQuarter),
                Money(activity.TotalSpentSoFar),
            };
            row.AddRange(quarters.Select(q => Money(activity.Forecast.TryGetValue(q, out var value) ? value : null)));
            rows.Add(row);
        }

        var hasOther = funding.OtherActualThisQuarter is not null || funding.OtherForecast.Values.Any(v => v is not null);
        if (hasOther)
        {
            var other = new List<string> { "Other (not linked to an activity)", Money(funding.OtherActualThisQuarter), string.Empty };
            other.AddRange(quarters.Select(q => Money(funding.OtherForecast.TryGetValue(q, out var value) ? value : null)));
            rows.Add(other);
        }

        var summary = new List<ReportSnapshotRow>
        {
            new("Total grant allocation", Money(funding.Summary.TotalGrantAllocation)),
            new("Total spend this quarter", Money(funding.Summary.TotalSpendThisQuarter)),
            new("Expected spend for the year", Money(funding.Summary.ExpectedSpendForYear)),
            new("Spend to date", Money(funding.Summary.SpendToDate)),
        };

        return new ReportSnapshotFunding(header, rows, summary);
    }

    private static string Money(decimal? amount) => amount?.ToString("0.00", CultureInfo.InvariantCulture) ?? string.Empty;
}