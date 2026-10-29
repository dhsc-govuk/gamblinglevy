using Domain.Activities;
using Domain.Common;
using Domain.Users;

namespace Domain.Submissions;

// Per-activity progress inside the "Enter activity results" section of one submission (unique per submission +
// activity). Modelled as a separate aggregate with id-only references and its own repository (the ActivityAnswer
// precedent) so it persists without tracking conflicts against the Submission graph.
public class SubmissionActivityReport : IEntity<SubmissionActivityReportId>
{
    public SubmissionActivityReportId Id { get; private set; }
    public SubmissionId SubmissionId { get; private set; }
    public ActivityId ActivityId { get; private set; }
    public SubmissionSectionStatus Status { get; private set; }
    public UserId CreatedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private SubmissionActivityReport(
        SubmissionActivityReportId id,
        SubmissionId submissionId,
        ActivityId activityId,
        SubmissionSectionStatus status,
        UserId createdBy,
        DateTime createdAt,
        DateTime? updatedAt)
    {
        Id = id;
        SubmissionId = submissionId;
        ActivityId = activityId;
        Status = status;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public static SubmissionActivityReport Start(
        SubmissionActivityReportId id,
        SubmissionId submissionId,
        ActivityId activityId,
        UserId createdBy,
        DateTime utcNow)
    {
        Guard.NotNull(id, nameof(id));
        Guard.NotNull(submissionId, nameof(submissionId));
        Guard.NotNull(activityId, nameof(activityId));
        Guard.NotNull(createdBy, nameof(createdBy));

        return new SubmissionActivityReport(
            id,
            submissionId,
            activityId,
            SubmissionSectionStatus.InProgress,
            createdBy,
            utcNow,
            null);
    }

    public void MarkInProgress(DateTime utcNow)
    {
        Status = SubmissionSectionStatus.InProgress;
        UpdatedAt = utcNow;
    }

    public void MarkCompleted(DateTime utcNow)
    {
        Status = SubmissionSectionStatus.Completed;
        UpdatedAt = utcNow;
    }
}