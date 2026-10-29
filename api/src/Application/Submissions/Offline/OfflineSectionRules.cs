using System.Globalization;
using Domain.DataCollectionForms;

namespace Application.Submissions.Offline;

// The amounts an uploaded funding file gives, in the shapes FundingSpendValidator and FundingSpendEntries take.
public sealed record OfflineFundingAmounts(
    IReadOnlyDictionary<Guid, decimal?> Actuals,
    decimal? OtherActual,
    IReadOnlyDictionary<(Guid ActivityId, int Quarter), decimal?> Forecasts,
    IReadOnlyDictionary<int, decimal?> OtherForecasts);

// Each section's own validator, run over the answers read from an upload with required answers enforced — the same
// rules and messages as the section's Check page on the web, because a valid upload completes the section.
public static class OfflineSectionRules
{
    // transitive is BLUM_9087: on, a question whose parent is hidden is hidden too.
    public static IReadOnlyList<OfflineCodeError> ActivityResults(IReadOnlyList<OfflineRowAnswers> rows, bool transitive) =>
        rows.SelectMany(row => Protocol.SubmissionAnswerValidator
                .Validate(row.Row.Bindings.Select(binding => binding.Question).ToList(), row.Answers, enforceRequired: true, transitive)
                .Select(error => new OfflineCodeError(row.Row, error.QuestionCode, error.Message)))
            .ToList();

    public static IReadOnlyList<OfflineCodeError> StrategicFeedback(IReadOnlyList<DcFormQuestion> questions, IReadOnlyList<OfflineRowAnswers> rows) =>
        rows.SelectMany(row => StrategicFeedbackAnswerValidator
                .Validate(questions, row.Answers, enforceRequired: true)
                .Select(error => new OfflineCodeError(row.Row, error.QuestionCode, error.Message)))
            .ToList();

    // The amounts come from FundingAmounts over the same rows; the upload reads them once and writes what it checked.
    public static IReadOnlyList<OfflineCodeError> FundingSpent(
        OfflineSheet sheet,
        FundingSpendContext context,
        IReadOnlyList<int> remainingQuarters,
        OfflineFundingAmounts amounts)
    {
        var errors = FundingSpendValidator.ValidateActuals(context.ActualSpendSubjects, amounts.Actuals, amounts.OtherActual, enforceRequired: true)
            .Concat(FundingSpendValidator.ValidateForecast(
                context.ForecastSpendSubjects, remainingQuarters, amounts.Forecasts, amounts.OtherForecasts, enforceRequired: true));

        // Every amount's error key is the code of the cell that holds it; a key with no row (an activity left out
        // of the file) is reported as a missing row instead.
        var rowByCode = sheet.Rows
            .SelectMany(row => row.Bindings.Select(binding => (binding.Question.Code, Row: row)))
            .ToDictionary(pair => pair.Code, pair => pair.Row, StringComparer.Ordinal);

        return errors
            .Where(error => rowByCode.ContainsKey(error.QuestionCode))
            .Select(error => new OfflineCodeError(rowByCode[error.QuestionCode], error.QuestionCode, error.Message))
            .ToList();
    }

    public static OfflineFundingAmounts FundingAmounts(
        FundingSpendContext context,
        IReadOnlyList<int> remainingQuarters,
        IReadOnlyList<OfflineRowAnswers> rows)
    {
        var byCode = rows
            .SelectMany(row => row.Answers)
            .ToDictionary(answer => answer.Key, answer => answer.Value, StringComparer.Ordinal);

        decimal? Amount(string code) =>
            byCode.TryGetValue(code, out var value) && decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
                ? amount
                : null;

        var actuals = context.ActualSpendActivities.ToDictionary(activity => activity.Id.Value, activity => Amount($"actual-{activity.Id.Value}"));
        var forecasts = new Dictionary<(Guid ActivityId, int Quarter), decimal?>();
        foreach (var activity in context.ForecastSpendActivities)
        {
            foreach (var quarter in remainingQuarters)
            {
                forecasts[(activity.Id.Value, quarter)] = Amount($"forecast-{activity.Id.Value}-q{quarter}");
            }
        }

        var otherForecasts = remainingQuarters.ToDictionary(quarter => quarter, quarter => Amount($"other-forecast-q{quarter}"));

        return new OfflineFundingAmounts(actuals, Amount("other-spend"), forecasts, otherForecasts);
    }
}