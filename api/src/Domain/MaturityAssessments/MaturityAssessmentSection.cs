using Domain.Common;

namespace Domain.MaturityAssessments;

// One domain of a maturity assessment as a task-list section, snapshotted from the static protocol's section codes
// when the assessment is started. SectionCode is the protocol's stable code (la_capacity, needs_assessment, ...),
// so the row stays resolvable across environments and protocol revisions. Owned by MaturityAssessment — mutations
// go through the aggregate root.
public class MaturityAssessmentSection : IEntity<MaturityAssessmentSectionId>
{
    public MaturityAssessmentSectionId Id { get; private set; }
    public MaturityAssessmentId AssessmentId { get; private set; }
    public string SectionCode { get; private set; }
    public MaturityAssessmentSectionStatus Status { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private MaturityAssessmentSection(
        MaturityAssessmentSectionId id,
        MaturityAssessmentId assessmentId,
        string sectionCode,
        MaturityAssessmentSectionStatus status,
        DateTime? updatedAt)
    {
        Id = id;
        AssessmentId = assessmentId;
        SectionCode = sectionCode;
        Status = status;
        UpdatedAt = updatedAt;
    }

    internal static MaturityAssessmentSection Create(
        MaturityAssessmentSectionId id,
        MaturityAssessmentId assessmentId,
        string sectionCode)
    {
        Guard.NotNull(id, nameof(id));
        Guard.NotNull(assessmentId, nameof(assessmentId));
        Guard.NotNullOrEmptyOrWhiteSpace(sectionCode, nameof(sectionCode));

        return new MaturityAssessmentSection(
            id,
            assessmentId,
            sectionCode.Trim(),
            MaturityAssessmentSectionStatus.NotStarted,
            updatedAt: null);
    }

    internal void SetStatus(MaturityAssessmentSectionStatus status, DateTime utcNow)
    {
        Guard.NotNull(status, nameof(status));

        Status = status;
        UpdatedAt = utcNow;
    }
}