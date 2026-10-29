namespace Application.Submissions.Offline;

// The three sections an LA can complete offline, by module code, with the slug their template route and file name
// use.
public static class OfflineSections
{
    public const string ActivityResultsSlug = "activity-results";
    public const string FundingSpentSlug = "funding-spent";
    public const string StrategicFeedbackSlug = "strategic-feedback";

    private static readonly IReadOnlyDictionary<string, string> SlugByModule = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [SubmissionModules.ActivityKeyResults] = ActivityResultsSlug,
        [SubmissionModules.FundingSpent] = FundingSpentSlug,
        [SubmissionModules.StrategicFeedback] = StrategicFeedbackSlug,
    };

    public static bool IsSupported(string moduleCode) => SlugByModule.ContainsKey(moduleCode);

    public static string SlugFor(string moduleCode) => SlugByModule[moduleCode];
}