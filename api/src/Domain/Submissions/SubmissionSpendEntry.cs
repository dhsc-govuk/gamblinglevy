using Domain.Activities;
using Domain.Common;
using Domain.Users;

namespace Domain.Submissions;

// One levy-spend amount captured in the "Record funding spent" section of a submission. Modelled as a separate
// aggregate with id-only references and its own repository (the SubmissionActivityAnswer precedent) so it persists
// without tracking conflicts against the Submission graph.
//
// A null ActivityId is the non-activity "Other spend" line. Kind = Actual is the amount spent in the reporting
// quarter (Quarter is null); Kind = Forecast is the expected spend for a specific future Quarter (1..4). Rows are
// replaced wholesale per submission + kind on every save.
public class SubmissionSpendEntry : IEntity<SubmissionSpendEntryId>
{
    public SubmissionSpendEntryId Id { get; private set; }
    public SubmissionId SubmissionId { get; private set; }
    public ActivityId? ActivityId { get; private set; }
    public SpendEntryKind Kind { get; private set; }
    public int? Quarter { get; private set; }
    public decimal Amount { get; private set; }
    public UserId CreatedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private SubmissionSpendEntry(
        SubmissionSpendEntryId id,
        SubmissionId submissionId,
        ActivityId? activityId,
        SpendEntryKind kind,
        int? quarter,
        decimal amount,
        UserId createdBy,
        DateTime createdAt)
    {
        Id = id;
        SubmissionId = submissionId;
        ActivityId = activityId;
        Kind = kind;
        Quarter = quarter;
        Amount = amount;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
    }

    // Actual levy spent this quarter for an activity (or, with a null activityId, the "Other spend this quarter").
    public static SubmissionSpendEntry Actual(
        SubmissionSpendEntryId id,
        SubmissionId submissionId,
        ActivityId? activityId,
        decimal amount,
        UserId createdBy,
        DateTime utcNow)
    {
        Guard.NotNull(id, nameof(id));
        Guard.NotNull(submissionId, nameof(submissionId));
        Guard.NotNull(createdBy, nameof(createdBy));
        Guard.GreaterThanOrEqual(amount, 0m, nameof(amount));

        return new SubmissionSpendEntry(
            id, submissionId, activityId, SpendEntryKind.Actual, null, amount, createdBy, utcNow);
    }

    // Expected spend for a given future quarter for an activity (or, with a null activityId, "Other planned spend").
    public static SubmissionSpendEntry Forecast(
        SubmissionSpendEntryId id,
        SubmissionId submissionId,
        ActivityId? activityId,
        int quarter,
        decimal amount,
        UserId createdBy,
        DateTime utcNow)
    {
        Guard.NotNull(id, nameof(id));
        Guard.NotNull(submissionId, nameof(submissionId));
        Guard.NotNull(createdBy, nameof(createdBy));
        Guard.GreaterThanOrEqual(amount, 0m, nameof(amount));

        if (quarter is < 1 or > 4)
        {
            throw new ArgumentOutOfRangeException(nameof(quarter), quarter, "Quarter must be between 1 and 4.");
        }

        return new SubmissionSpendEntry(
            id, submissionId, activityId, SpendEntryKind.Forecast, quarter, amount, createdBy, utcNow);
    }
}