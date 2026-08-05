using Application.Forms.Answers;
using Domain.Activities;

namespace Application.Submissions;

// Validates "Record funding spent" amounts. Mirrors the frontend validateMoney so the two never disagree: every
// supplied amount must be £0 or more (£0 is allowed — the design tells users to enter £0 for nothing spent), and on
// complete every reportable activity plus the non-activity "other" line must have a value. Required is only
// enforced when completing ("Save and come back later" passes enforceRequired: false). Error keys match the
// frontend so each error routes back to its owning screen.
public static class FundingSpendValidator
{
    // Technical upper bound that keeps amounts well within the numeric(18,2) column (and far above any realistic
    // levy spend), so an over-large amount is a clean 422 rather than a DbUpdateException / 500 on save.
    private const decimal MaxAmount = 1_000_000_000m;

    public static IReadOnlyList<AnswerValidationError> ValidateActuals(
        IReadOnlyCollection<ActivityId> reportableIds,
        IReadOnlyDictionary<Guid, decimal?> activityAmounts,
        decimal? otherAmount,
        bool enforceRequired)
    {
        var errors = new List<AnswerValidationError>();

        foreach (var id in reportableIds)
        {
            activityAmounts.TryGetValue(id.Value, out var amount);
            ValidateAmount($"actual-{id.Value}", "the amount spent", amount, enforceRequired, errors);
        }

        ValidateAmount("other-spend", "the other spend this quarter", otherAmount, enforceRequired, errors);

        return errors;
    }

    public static IReadOnlyList<AnswerValidationError> ValidateForecast(
        IReadOnlyCollection<ActivityId> reportableIds,
        IReadOnlyList<int> remainingQuarters,
        IReadOnlyDictionary<(Guid ActivityId, int Quarter), decimal?> activityAmounts,
        IReadOnlyDictionary<int, decimal?> otherAmounts,
        bool enforceRequired)
    {
        var errors = new List<AnswerValidationError>();

        foreach (var id in reportableIds)
        {
            foreach (var quarter in remainingQuarters)
            {
                activityAmounts.TryGetValue((id.Value, quarter), out var amount);
                ValidateAmount($"forecast-{id.Value}-q{quarter}", "the amount you expect to spend", amount, enforceRequired, errors);
            }
        }

        foreach (var quarter in remainingQuarters)
        {
            otherAmounts.TryGetValue(quarter, out var amount);
            ValidateAmount($"other-forecast-q{quarter}", "the other planned spend", amount, enforceRequired, errors);
        }

        return errors;
    }

    private static void ValidateAmount(
        string code,
        string label,
        decimal? amount,
        bool enforceRequired,
        List<AnswerValidationError> errors)
    {
        if (amount is null)
        {
            if (enforceRequired)
            {
                errors.Add(new AnswerValidationError(code, $"Enter {label}"));
            }

            return;
        }

        if (amount < 0m)
        {
            errors.Add(new AnswerValidationError(code, "Enter an amount of £0 or more"));
        }
        else if (amount > MaxAmount)
        {
            errors.Add(new AnswerValidationError(code, "Enter an amount of £1,000,000,000 or less"));
        }
    }
}