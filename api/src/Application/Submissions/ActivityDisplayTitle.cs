using Domain.Activities;

namespace Application.Submissions;

// The name an activity goes by on the quarterly report: its title, then the custom "Other" text, then the
// activity type's name. Mirrors the activities list fallback, so a row reads the same on the report screens and in
// the offline spreadsheet (BLUM-8310).
public static class ActivityDisplayTitle
{
    public static string Resolve(Activity activity, string? taskName)
    {
        if (!string.IsNullOrWhiteSpace(activity.Title))
        {
            return activity.Title!;
        }

        if (!string.IsNullOrWhiteSpace(activity.CustomTaskText))
        {
            return activity.CustomTaskText!;
        }

        return string.IsNullOrWhiteSpace(taskName) ? "Untitled activity" : taskName!;
    }
}