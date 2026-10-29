using Domain.Common;
using Domain.DataCollectionForms;

namespace Domain.Submissions;

// One task-list section of a quarterly-report submission, snapshotted from the collection's assigned form modules
// when the report is started. ModuleCode is copied so the section stays resolvable across environments and survives
// the module being archived. ReviewRequired marks a started section the LA must revisit because the activity list
// changed after it was confirmed. Owned by Submission (mutations go through the aggregate root).
public class SubmissionSection : IEntity<SubmissionSectionId>
{
    public SubmissionSectionId Id { get; private set; }
    public SubmissionId SubmissionId { get; private set; }
    public FormModuleId FormModuleId { get; private set; }
    public string ModuleCode { get; private set; }
    public SubmissionSectionStatus Status { get; private set; }
    public bool ReviewRequired { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private SubmissionSection(
        SubmissionSectionId id,
        SubmissionId submissionId,
        FormModuleId formModuleId,
        string moduleCode,
        SubmissionSectionStatus status,
        bool reviewRequired,
        DateTime? updatedAt)
    {
        Id = id;
        SubmissionId = submissionId;
        FormModuleId = formModuleId;
        ModuleCode = moduleCode;
        Status = status;
        ReviewRequired = reviewRequired;
        UpdatedAt = updatedAt;
    }

    internal static SubmissionSection Create(
        SubmissionSectionId id,
        SubmissionId submissionId,
        FormModuleId formModuleId,
        string moduleCode)
    {
        Guard.NotNull(id, nameof(id));
        Guard.NotNull(submissionId, nameof(submissionId));
        Guard.NotNull(formModuleId, nameof(formModuleId));
        Guard.NotNullOrEmptyOrWhiteSpace(moduleCode, nameof(moduleCode));

        return new SubmissionSection(
            id,
            submissionId,
            formModuleId,
            moduleCode.Trim(),
            SubmissionSectionStatus.NotStarted,
            reviewRequired: false,
            updatedAt: null);
    }

    internal void SetStatus(SubmissionSectionStatus status, DateTime utcNow)
    {
        Guard.NotNull(status, nameof(status));

        Status = status;
        UpdatedAt = utcNow;
    }

    internal void FlagForReview(DateTime utcNow)
    {
        ReviewRequired = true;
        UpdatedAt = utcNow;
    }

    internal void ClearReviewFlag(DateTime utcNow)
    {
        ReviewRequired = false;
        UpdatedAt = utcNow;
    }
}