namespace Application.Activities.Commands;

// Shared by the Create/Update activity validators. An activity's start date is captured as a month/year
// pair (no day component); for Active activities it must not be in the future. Only strictly-later
// months/years count as future, so a date within the current month is allowed. Uses UTC to match the
// client's UTC-based check so the two agree on the current month around midnight UTC. Nullable inputs
// compare as false, so callers do not need to pre-guard against nulls.
public static class ActivityStartDate
{
    public static bool IsInFuture(int? year, int? month)
    {
        var now = DateTime.UtcNow;
        return year > now.Year || (year == now.Year && month > now.Month);
    }
}