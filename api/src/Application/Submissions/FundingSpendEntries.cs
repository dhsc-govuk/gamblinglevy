using Domain.Submissions;

namespace Application.Submissions;

// Builds the spend rows a "Record funding spent" save replaces the submission's rows with, from amounts already
// validated by FundingSpendValidator. Shared by the two web screens and the offline upload (BLUM-8310) so every
// path writes exactly the same rows: one per activity of the screen's own set that has an amount, plus the
// non-activity "Other" line (ActivityId null). A blank amount writes no row.
public static class FundingSpendEntries
{
    public static IReadOnlyList<SubmissionSpendEntry> Actuals(
        FundingSpendContext context,
        IReadOnlyDictionary<Guid, decimal?> activityAmounts,
        decimal? otherAmount,
        DateTime utcNow)
    {
        var entries = new List<SubmissionSpendEntry>();
        foreach (var activity in context.ActualSpendActivities)
        {
            if (activityAmounts.TryGetValue(activity.Id.Value, out var amount) && amount is decimal value)
            {
                entries.Add(SubmissionSpendEntry.Actual(SubmissionSpendEntryId.New(), context.Submission.Id, activity.Id, value, context.ActorId, utcNow));
            }
        }

        if (otherAmount is decimal other)
        {
            entries.Add(SubmissionSpendEntry.Actual(SubmissionSpendEntryId.New(), context.Submission.Id, null, other, context.ActorId, utcNow));
        }

        return entries;
    }

    public static IReadOnlyList<SubmissionSpendEntry> Forecast(
        FundingSpendContext context,
        IReadOnlyList<int> remainingQuarters,
        IReadOnlyDictionary<(Guid ActivityId, int Quarter), decimal?> activityAmounts,
        IReadOnlyDictionary<int, decimal?> otherAmounts,
        DateTime utcNow)
    {
        var entries = new List<SubmissionSpendEntry>();
        foreach (var activity in context.ForecastSpendActivities)
        {
            foreach (var quarter in remainingQuarters)
            {
                if (activityAmounts.TryGetValue((activity.Id.Value, quarter), out var amount) && amount is decimal value)
                {
                    entries.Add(SubmissionSpendEntry.Forecast(SubmissionSpendEntryId.New(), context.Submission.Id, activity.Id, quarter, value, context.ActorId, utcNow));
                }
            }
        }

        foreach (var quarter in remainingQuarters)
        {
            if (otherAmounts.TryGetValue(quarter, out var amount) && amount is decimal value)
            {
                entries.Add(SubmissionSpendEntry.Forecast(SubmissionSpendEntryId.New(), context.Submission.Id, null, quarter, value, context.ActorId, utcNow));
            }
        }

        return entries;
    }
}