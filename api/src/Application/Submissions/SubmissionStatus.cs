namespace Application.Submissions;

// The per-organisation submission status surfaced to DHSC for a reporting window. Unlike CollectionStatus this is
// not a stored or domain concept — it is derived for the DHSC submission-visibility screens by combining whether the
// organisation has a Submission row, whether that submission has been finalised (SubmittedAt set), and the
// collection's submission deadline relative to today. See SubmissionStatusRules.Derive.
public static class SubmissionStatuses
{
    // No submission row for this org/collection (and the deadline has not yet passed).
    public const string NotStarted = "not_started";

    // A submission has been started (row exists) but not yet finalised (and the deadline has not yet passed).
    public const string InProgress = "in_progress";

    // The submission has been finalised — SubmittedAt is set. "Completed" in the UI.
    public const string Completed = "completed";

    // The deadline has passed and the org has not finalised a submission (whether or not one was started).
    public const string Overdue = "overdue";

    // Human-readable label for the status constant — used by the DHSC submissions export CSV. Mirrors the frontend
    // SUBMISSION_STATUS_LABEL map so the spreadsheet and the screens read the same.
    public static string Label(string status) => status switch
    {
        NotStarted => "Not started",
        InProgress => "In progress",
        Completed => "Completed",
        Overdue => "Overdue",
        _ => status,
    };
}

public static class SubmissionStatusRules
{
    // Derives the DHSC-facing per-org status:
    //   submitted (SubmittedAt set)            -> completed (a finalised submission wins, even if it was late)
    //   not submitted, past the deadline       -> overdue   (whether or not a draft row exists)
    //   submission row started, within window  -> in_progress
    //   no submission row, within window       -> not_started
    public static string Derive(bool hasSubmission, DateTime? submittedAt, DateOnly? deadline)
    {
        if (submittedAt is not null)
        {
            return SubmissionStatuses.Completed;
        }

        var isOverdue = deadline is { } d && DateOnly.FromDateTime(DateTime.UtcNow) > d;
        if (isOverdue)
        {
            return SubmissionStatuses.Overdue;
        }

        return hasSubmission ? SubmissionStatuses.InProgress : SubmissionStatuses.NotStarted;
    }
}