using System.Globalization;
using Application.Submissions.Protocol;
using Domain.Forms;
using Domain.Submissions;

namespace Application.Submissions.Offline;

// "Record funding spent" as a spreadsheet: one row per activity the section asks about and an "Other spend" row, with
// the amount spent this quarter and the expected spend for each quarter still to come.
//
// Each amount cell is bound to a money question whose Code is FundingSpendValidator's key for that amount, so an
// upload is validated by the same rules and messages as the two web screens and every error maps back to its cell.
// A Planned activity is only on the forecast screen, so its "spent this quarter" cell says N/A; in Q4 there is no
// forecast left to give, so a Planned activity has nothing to answer and is left out.
public static class FundingSpentSheetBuilder
{
    public const string ReferenceKey = "ref";
    public const string ActivityKey = "activity";
    public const string StatusKey = "status";
    public const string ErrorsKey = "errors";
    public const string ActualKey = "actual";
    public const string OtherReference = "OTHER";

    private const string Section = "funding";

    public static string ForecastKey(int quarter) => $"forecast_q{quarter}";

    public static OfflineSheet Build(
        Guid collectionId,
        Guid organisationId,
        FundingSpendContext context,
        IReadOnlyList<int> remainingQuarters,
        IReadOnlyDictionary<Guid, string> titles,
        (DateOnly Start, DateOnly End) reportingPeriod,
        IReadOnlyDictionary<int, (DateOnly Start, DateOnly End)> quarterPeriods,
        IReadOnlyList<SubmissionSpendEntry> savedEntries)
    {
        var actualIds = context.ActualSpendActivities.Select(activity => activity.Id.Value).ToHashSet();
        var rowActivities = remainingQuarters.Count > 0 ? context.ForecastSpendActivities : context.ActualSpendActivities;
        var hasPlannedRows = rowActivities.Any(activity => !actualIds.Contains(activity.Id.Value));

        var specs = new List<OfflineColumnSpec>
        {
            new(ReferenceKey, OfflineColumnKind.Reference, "Reference (do not change)", Guidance: Preamble),
            new(ActivityKey, OfflineColumnKind.ReadOnly, "Activity (do not change)", Guidance: OfflineSheetText.ReadOnlyGuidance),
            new(StatusKey, OfflineColumnKind.ReadOnly, "Status (do not change)", Guidance: OfflineSheetText.ReadOnlyGuidance),
            new(ErrorsKey, OfflineColumnKind.Errors, OfflineSheetText.ErrorsHeading, Guidance: OfflineSheetText.ErrorsGuidance),
            new(
                ActualKey,
                OfflineColumnKind.Question,
                "Amount spent this quarter (£)",
                Money("actual", "Amount spent this quarter"),
                $"The levy spent between {Date(reportingPeriod.Start)} and {Date(reportingPeriod.End)}."
                + (hasPlannedRows ? " Planned activities say N/A." : string.Empty)),
        };

        foreach (var quarter in remainingQuarters)
        {
            var (start, end) = quarterPeriods[quarter];
            specs.Add(new OfflineColumnSpec(
                ForecastKey(quarter),
                OfflineColumnKind.Question,
                $"Expected spend in Quarter {quarter} (£)",
                Money($"forecast-q{quarter}", $"Expected spend in Quarter {quarter}"),
                $"The levy you expect to spend in Quarter {quarter}, {Date(start)} to {Date(end)}."));
        }

        var actuals = new Dictionary<Guid, decimal>();
        decimal? otherActual = null;
        var forecasts = new Dictionary<(Guid, int), decimal>();
        var otherForecasts = new Dictionary<int, decimal>();
        foreach (var entry in savedEntries)
        {
            if (entry.Kind.IsActual)
            {
                if (entry.ActivityId is null)
                {
                    otherActual = entry.Amount;
                }
                else
                {
                    actuals[entry.ActivityId.Value] = entry.Amount;
                }
            }
            else if (entry.Quarter is int quarter)
            {
                if (entry.ActivityId is null)
                {
                    otherForecasts[quarter] = entry.Amount;
                }
                else
                {
                    forecasts[(entry.ActivityId.Value, quarter)] = entry.Amount;
                }
            }
        }

        var rows = new List<OfflineRow>();
        foreach (var activity in rowActivities)
        {
            var id = activity.Id.Value;
            var title = titles.TryGetValue(id, out var name) ? name : "Untitled activity";
            var bindings = new List<OfflineBinding>();
            var values = new Dictionary<string, string?>(StringComparer.Ordinal);

            if (actualIds.Contains(id))
            {
                var code = $"actual-{id}";
                bindings.Add(new OfflineBinding(ActualKey, Money(code, $"Amount spent on {title}")));
                if (actuals.TryGetValue(id, out var amount))
                {
                    values[code] = OfflineCellValues.FormatMoney(amount);
                }
            }

            foreach (var quarter in remainingQuarters)
            {
                var code = $"forecast-{id}-q{quarter}";
                bindings.Add(new OfflineBinding(ForecastKey(quarter), Money(code, $"Expected spend on {title} for Quarter {quarter}")));
                if (forecasts.TryGetValue((id, quarter), out var amount))
                {
                    values[code] = OfflineCellValues.FormatMoney(amount);
                }
            }

            rows.Add(new OfflineRow(
                id.ToString(),
                title,
                new Dictionary<string, string>(StringComparer.Ordinal) { [ActivityKey] = title, [StatusKey] = OfflineSheetText.StatusLabel(activity.Status) },
                bindings,
                values));
        }

        var otherBindings = new List<OfflineBinding> { new(ActualKey, Money("other-spend", "Other spend this quarter")) };
        var otherValues = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (otherActual is decimal other)
        {
            otherValues["other-spend"] = OfflineCellValues.FormatMoney(other);
        }

        foreach (var quarter in remainingQuarters)
        {
            var code = $"other-forecast-q{quarter}";
            otherBindings.Add(new OfflineBinding(ForecastKey(quarter), Money(code, $"Other planned spend for Quarter {quarter}")));
            if (otherForecasts.TryGetValue(quarter, out var amount))
            {
                otherValues[code] = OfflineCellValues.FormatMoney(amount);
            }
        }

        rows.Add(new OfflineRow(
            OtherReference,
            "The ‘Other spend’ row",
            new Dictionary<string, string>(StringComparer.Ordinal) { [ActivityKey] = "Other spend not linked to an activity", [StatusKey] = string.Empty },
            otherBindings,
            otherValues));

        var fingerprint = OfflineIdentity.ComputeFingerprint(
            rows.Select(row => $"{row.Reference}:{(row.BindingFor(ActualKey) is null ? 0 : 1)}")
                .Append($"quarters:{string.Join(',', remainingQuarters)}"));

        return OfflineSheetAssembler.Assemble(
            SubmissionModules.FundingSpent, collectionId, organisationId, fingerprint, specs, rows, singleRow: false);
    }

    // An amount cell. Code is FundingSpendValidator's error key; the label is only what the web form would call it.
    private static ResolvedQuestion Money(string code, string label) => new(
        Guid.Empty,
        code,
        label,
        null,
        QuestionTypes.Currency,
        IsRequired: true,
        0,
        null,
        null,
        [],
        null,
        null,
        IsPercentage: false,
        null,
        null,
        Section);

    private static string Date(DateOnly date) => date.ToString("d MMMM yyyy", CultureInfo.GetCultureInfo("en-GB"));

    private const string Preamble =
        "How to answer: fill in one row for each activity, and the ‘Other spend’ row, from row 4. Do not change column A "
        + "or row 3 – we use them to match your answers to your report. N/A means no amount is needed there, so leave "
        + "it as it is. " + OfflineSheetText.SaveAdvice;
}