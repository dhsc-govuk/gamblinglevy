using Application.Forms.Answers;
using Domain.Activities;

namespace Application.Submissions;

// One activity a spend amount can be recorded against, with the name the Question Protocol interpolates into that
// amount's error messages ("Enter the amount spent on [ActivityName]").
//
// The fallback chain mirrors QuarterlyReportControllerService.ResolveDisplayTitle, which is what labels the row
// on screen: title, then the custom "Other ..." text, then the activity type's name. The type name matters —
// People-and-roles records are exempt from the mandatory-name rule, so they legitimately have no title, and
// without it every one of them would say "this activity" and match no row in the error summary.
public record FundingSpendSubject(ActivityId Id, string Name)
{
    public static FundingSpendSubject For(Activity activity, string? taskName) => new(
        activity.Id,
        FirstNonBlank(activity.Title, activity.CustomTaskText, taskName) ?? "this activity");

    private static string? FirstNonBlank(params string?[] candidates) =>
        candidates.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate))?.Trim();
}

// Validates "Record funding spent" amounts. Mirrors the frontend validateMoney so the two never disagree: every
// supplied amount must be a whole number of pounds, £0 or more (£0 is allowed — the design tells users to enter £0
// for nothing spent), and on complete every reportable activity plus the non-activity "other" line must have a
// value. Required is only enforced when completing ("Save and come back later" passes enforceRequired: false).
// Error keys match the frontend so each error routes back to its owning screen.
//
// The 13 August Question Protocol (BLUM-7620) dropped the "amount of 1 or more" rule, named the activity and
// quarter in every message, and banned pence. It also bans a typed "£", which only the client can see — the API
// receives an already-parsed number — so that rule lives in validateMoney alone.
public static class FundingSpendValidator
{
    // Technical upper bound that keeps amounts well within the numeric(18,2) column (and far above any realistic
    // levy spend), so an over-large amount is a clean 422 rather than a DbUpdateException / 500 on save.
    private const decimal MaxAmount = 1_000_000_000m;

    public static IReadOnlyList<AnswerValidationError> ValidateActuals(
        IReadOnlyCollection<FundingSpendSubject> reportableActivities,
        IReadOnlyDictionary<Guid, decimal?> activityAmounts,
        decimal? otherAmount,
        bool enforceRequired)
    {
        var errors = new List<AnswerValidationError>();

        foreach (var activity in reportableActivities)
        {
            activityAmounts.TryGetValue(activity.Id.Value, out var amount);
            ValidateAmount(
                $"actual-{activity.Id.Value}",
                $"the amount spent on {activity.Name}",
                $"Amount spent on {activity.Name}",
                amount,
                enforceRequired,
                errors);
        }

        // Sheet: the "Other spend this quarter" line has no error column of its own — its wording follows the
        // per-activity line with the activity name dropped.
        ValidateAmount("other-spend", "the other spend this quarter", "Other spend this quarter", otherAmount, enforceRequired, errors);

        return errors;
    }

    public static IReadOnlyList<AnswerValidationError> ValidateForecast(
        IReadOnlyCollection<FundingSpendSubject> reportableActivities,
        IReadOnlyList<int> remainingQuarters,
        IReadOnlyDictionary<(Guid ActivityId, int Quarter), decimal?> activityAmounts,
        IReadOnlyDictionary<int, decimal?> otherAmounts,
        bool enforceRequired)
    {
        var errors = new List<AnswerValidationError>();

        foreach (var activity in reportableActivities)
        {
            foreach (var quarter in remainingQuarters)
            {
                activityAmounts.TryGetValue((activity.Id.Value, quarter), out var amount);
                ValidateAmount(
                    $"forecast-{activity.Id.Value}-q{quarter}",
                    $"expected spend on {activity.Name} for Quarter {quarter}",
                    $"Expected spend on {activity.Name} for Quarter {quarter}",
                    amount,
                    enforceRequired,
                    errors);
            }
        }

        foreach (var quarter in remainingQuarters)
        {
            otherAmounts.TryGetValue(quarter, out var amount);
            ValidateAmount(
                $"other-forecast-q{quarter}",
                $"the amount of other planned spend for Quarter {quarter}",
                $"Other planned spend for Quarter {quarter}",
                amount,
                enforceRequired,
                errors);
        }

        return errors;
    }

    // requiredSubject reads after "Enter " (lower case, mid-sentence); sentenceSubject starts a sentence.
    private static void ValidateAmount(
        string code,
        string requiredSubject,
        string sentenceSubject,
        decimal? amount,
        bool enforceRequired,
        List<AnswerValidationError> errors)
    {
        if (amount is null)
        {
            if (enforceRequired)
            {
                errors.Add(new AnswerValidationError(code, $"Enter {requiredSubject}"));
            }

            return;
        }

        // Sign before shape: -1.50 is both negative and not whole, and reporting the pence first would make the
        // user correct it, resubmit, and only then be told the real problem.
        if (amount < 0m)
        {
            errors.Add(new AnswerValidationError(code, $"{sentenceSubject} must be £0 or more"));
        }
        else if (amount != decimal.Truncate(amount.Value))
        {
            errors.Add(new AnswerValidationError(code, $"{sentenceSubject} must be a whole number and not include pence"));
        }
        else if (amount > MaxAmount)
        {
            errors.Add(new AnswerValidationError(code, $"{sentenceSubject} must be £1,000,000,000 or less"));
        }
    }
}