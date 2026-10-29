using Domain.Activities;

namespace Application.Submissions.Offline;

// Copy shared by the three offline sheets.
public static class OfflineSheetText
{
    public const string ErrorsHeading = "Errors (leave blank)";

    public const string ErrorsGuidance =
        "Leave blank. If your file has problems when you upload it, we list them here in the copy of your file you can download.";

    public const string ReadOnlyGuidance = "Do not change. For information only.";

    public const string SaveAdvice =
        "When you have finished, save the file as ‘CSV UTF-8 (Comma delimited)’ and upload it on the ‘Manage your quarterly report’ page.";

    // An activity's status as the read-only Status cell shows it: "Active", "Closed", "Finished", "Planned".
    public static string StatusLabel(ActivityStatus? status)
    {
        var value = status?.ToString() ?? string.Empty;
        return value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];
    }
}