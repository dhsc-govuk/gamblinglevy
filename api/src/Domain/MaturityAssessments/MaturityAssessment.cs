using Domain.Collections;
using Domain.Common;
using Domain.Organisations;
using Domain.Users;

namespace Domain.MaturityAssessments;

// An LA's annual maturity assessment against one maturity-assessment collection (unique per collection +
// organisation). Owns the per-domain section snapshot that drives the task list; the answers are an id-only
// satellite (MaturityAssessmentAnswer) replaced per page, mirroring SubmissionFeedbackAnswer.
//
// Deliberately NOT a Submission: that aggregate's sections FK the DHSC form-builder modules, its submit needs a
// quarter, and its activity/funding satellites have no meaning here. The pure helpers the two share
// (SubmissionStatusRules, SubmissionAssignment, the answer validator) live in Application and take both.
public class MaturityAssessment : IEntity<MaturityAssessmentId>
{
    private readonly List<MaturityAssessmentSection> _sections = new();

    public MaturityAssessmentId Id { get; private set; }
    public CollectionId CollectionId { get; private set; }
    public OrganisationId OrganisationId { get; private set; }
    public DateTime? SubmittedAt { get; private set; }
    public UserId? SubmittedBy { get; private set; }
    public string? ReferenceNumber { get; private set; }

    // The rendered assessment frozen at submit time (opaque JSON): sections → pages → question/answer rows. Feeds the
    // LA read-only view, the DHSC detail and both exports, so a later quarterly report can never change what was
    // submitted (BLUM-7859 AC3). Null until submitted.
    public string? SnapshotJson { get; private set; }

    public UserId CreatedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public UserId? UpdatedBy { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    public IReadOnlyCollection<MaturityAssessmentSection> Sections => _sections.AsReadOnly();

    private MaturityAssessment(
        MaturityAssessmentId id,
        CollectionId collectionId,
        OrganisationId organisationId,
        UserId createdBy,
        DateTime createdAt)
    {
        Id = id;
        CollectionId = collectionId;
        OrganisationId = organisationId;
        SubmittedAt = null;
        SubmittedBy = null;
        ReferenceNumber = null;
        SnapshotJson = null;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
        UpdatedBy = null;
        UpdatedAt = null;
    }

    // Starts an assessment: one Not-started section per given section code. Pre-filled answers are written by the
    // Start command afterwards and deliberately leave every section Not started — the LA has not reviewed them yet.
    public static MaturityAssessment Start(
        MaturityAssessmentId id,
        CollectionId collectionId,
        OrganisationId organisationId,
        IReadOnlyList<string> sectionCodes,
        UserId createdBy,
        DateTime utcNow)
    {
        Guard.NotNull(id, nameof(id));
        Guard.NotNull(collectionId, nameof(collectionId));
        Guard.NotNull(organisationId, nameof(organisationId));
        Guard.NotEmpty(sectionCodes, nameof(sectionCodes));
        Guard.NotNull(createdBy, nameof(createdBy));

        var assessment = new MaturityAssessment(id, collectionId, organisationId, createdBy, utcNow);

        foreach (var sectionCode in sectionCodes)
        {
            assessment._sections.Add(MaturityAssessmentSection.Create(MaturityAssessmentSectionId.New(), id, sectionCode));
        }

        return assessment;
    }

    // The assessment has been finalised and is read-only — every write rejects a submitted assessment.
    public bool IsSubmitted => SubmittedAt is not null;

    // Every one of the given sections has reached Completed (the submit gate). The caller passes the collection's
    // current sections (BLUM-8093): a row for a section the admin has since removed is ignored, and a section added
    // after Start that has no row yet is incomplete. False for an empty set.
    public bool AreSectionsCompleted(IReadOnlyCollection<string> sectionCodes)
    {
        Guard.NotNull(sectionCodes, nameof(sectionCodes));

        return sectionCodes.Count > 0
            && sectionCodes.All(code => _sections.Any(s => s.SectionCode == code && s.Status.IsCompleted));
    }

    // The final, irreversible step. The command layer enforces the completeness/eligibility gates; the IsSubmitted
    // guard here is a defensive invariant against a double submit.
    public void Submit(string referenceNumber, UserId actor, DateTime utcNow)
    {
        Guard.NotNullOrEmptyOrWhiteSpace(referenceNumber, nameof(referenceNumber));
        Guard.NotNull(actor, nameof(actor));

        if (IsSubmitted)
        {
            throw new InvalidOperationException($"Maturity assessment {Id} has already been submitted.");
        }

        SubmittedAt = utcNow;
        SubmittedBy = actor;
        ReferenceNumber = referenceNumber;

        Touch(actor, utcNow);
    }

    // Re-stamps only the reference number during the submit retry loop (a concurrent submit in the same financial
    // year took our sequence). The submitted moment + actor are left untouched.
    public void AssignReference(string referenceNumber, UserId actor, DateTime utcNow)
    {
        Guard.NotNullOrEmptyOrWhiteSpace(referenceNumber, nameof(referenceNumber));
        Guard.NotNull(actor, nameof(actor));

        if (!IsSubmitted)
        {
            throw new InvalidOperationException($"Maturity assessment {Id} cannot be re-referenced before it is submitted.");
        }

        ReferenceNumber = referenceNumber;

        Touch(actor, utcNow);
    }

    public void AttachSnapshot(string? snapshotJson)
    {
        SnapshotJson = snapshotJson;
    }

    public void SetSectionStatus(string sectionCode, MaturityAssessmentSectionStatus status, UserId actor, DateTime utcNow)
    {
        Guard.NotNullOrEmptyOrWhiteSpace(sectionCode, nameof(sectionCode));
        Guard.NotNull(status, nameof(status));
        Guard.NotNull(actor, nameof(actor));

        // Section rows are created at Start from the collection's forms of that day. A form the admin adds to the
        // collection afterwards is listed by the read side as "Not started" (ToSectionSummaries), so its first write
        // creates the row here rather than failing — the handlers have already checked the code against the
        // collection's current forms (BLUM-8093).
        var section = _sections.FirstOrDefault(s => s.SectionCode == sectionCode);
        if (section is null)
        {
            section = MaturityAssessmentSection.Create(MaturityAssessmentSectionId.New(), Id, sectionCode);
            _sections.Add(section);
        }

        section.SetStatus(status, utcNow);

        Touch(actor, utcNow);
    }

    private void Touch(UserId actor, DateTime utcNow)
    {
        UpdatedBy = actor;
        UpdatedAt = utcNow;
    }
}