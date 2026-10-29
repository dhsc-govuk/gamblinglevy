using Domain.Collections;
using Domain.Common;
using Domain.DataCollectionForms;
using Domain.Organisations;
using Domain.Users;

namespace Domain.Submissions;

// An LA's quarterly-report submission against one data collection (unique per collection + organisation). Holds the
// "Manage your list of activities" confirmation step and the per-module section snapshot that drives the task list.
// Per-activity progress and answers for the "Enter activity results" section are separate satellites
// (SubmissionActivityReport / SubmissionActivityAnswer) with id-only references, mirroring ActivityAnswer.
// The final submit step is out of scope for BLUM-6611 — sections only ever reach Completed here.
public class Submission : IEntity<SubmissionId>
{
    private readonly List<SubmissionSection> _sections = new();

    public SubmissionId Id { get; private set; }
    public CollectionId CollectionId { get; private set; }
    public OrganisationId OrganisationId { get; private set; }
    public DateTime? ActivitiesConfirmedAt { get; private set; }
    public UserId? ActivitiesConfirmedBy { get; private set; }
    public bool ActivitiesReviewRequired { get; private set; }
    public DateTime? SubmittedAt { get; private set; }
    public UserId? SubmittedBy { get; private set; }
    public string? ReferenceNumber { get; private set; }

    // The rendered "copy for your records" frozen at submit time (opaque JSON). Null until submitted, and null for
    // legacy submissions finalised before snapshots existed — the export falls back to a live render in that case.
    public string? ReportSnapshotJson { get; private set; }

    public UserId CreatedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public UserId? UpdatedBy { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    public IReadOnlyCollection<SubmissionSection> Sections => _sections.AsReadOnly();

    private Submission(
        SubmissionId id,
        CollectionId collectionId,
        OrganisationId organisationId,
        UserId createdBy,
        DateTime createdAt)
    {
        Id = id;
        CollectionId = collectionId;
        OrganisationId = organisationId;
        ActivitiesConfirmedAt = null;
        ActivitiesConfirmedBy = null;
        ActivitiesReviewRequired = false;
        SubmittedAt = null;
        SubmittedBy = null;
        ReferenceNumber = null;
        ReportSnapshotJson = null;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
        UpdatedBy = null;
        UpdatedAt = null;
    }

    // Starts a report: snapshots one Not-started section per form module assigned to the collection at this moment.
    // Later admin edits to the collection's modules deliberately do not reshape an in-flight submission.
    public static Submission Start(
        SubmissionId id,
        CollectionId collectionId,
        OrganisationId organisationId,
        IReadOnlyList<(FormModuleId ModuleId, string ModuleCode)> assignedModules,
        UserId createdBy,
        DateTime utcNow)
    {
        Guard.NotNull(id, nameof(id));
        Guard.NotNull(collectionId, nameof(collectionId));
        Guard.NotNull(organisationId, nameof(organisationId));
        Guard.NotNull(assignedModules, nameof(assignedModules));
        Guard.NotNull(createdBy, nameof(createdBy));

        var submission = new Submission(id, collectionId, organisationId, createdBy, utcNow);

        foreach (var (moduleId, moduleCode) in assignedModules)
        {
            submission._sections.Add(SubmissionSection.Create(SubmissionSectionId.New(), id, moduleId, moduleCode));
        }

        return submission;
    }

    public bool AreActivitiesConfirmed => ActivitiesConfirmedAt is not null;

    // The report has been finalised and is read-only — every write rejects a submitted report.
    public bool IsSubmitted => SubmittedAt is not null;

    // Every assigned section has reached Completed (the submit gate). False for a report with no sections.
    public bool AreAllSectionsCompleted => _sections.Count > 0 && _sections.All(s => s.Status.IsCompleted);

    // "Submit your quarterly report" — the final, irreversible step. Records the submission moment, the acting user
    // and the generated reference number; the report is read-only afterwards. The command layer enforces the
    // completeness/eligibility gates; the IsSubmitted guard here is a defensive invariant against a double submit.
    public void Submit(string referenceNumber, UserId actor, DateTime utcNow)
    {
        Guard.NotNullOrEmptyOrWhiteSpace(referenceNumber, nameof(referenceNumber));
        Guard.NotNull(actor, nameof(actor));

        if (IsSubmitted)
        {
            throw new InvalidOperationException($"Submission {Id} has already been submitted.");
        }

        SubmittedAt = utcNow;
        SubmittedBy = actor;
        ReferenceNumber = referenceNumber;

        Touch(actor, utcNow);
    }

    // Re-stamps only the reference number during the submit retry loop: a concurrent submit in the same financial
    // year + quarter took our sequence and the unique index rejected our save, so the handler recomputes the next
    // sequence and re-saves. The submitted moment + actor (set once by Submit) are deliberately left untouched.
    public void AssignReference(string referenceNumber, UserId actor, DateTime utcNow)
    {
        Guard.NotNullOrEmptyOrWhiteSpace(referenceNumber, nameof(referenceNumber));
        Guard.NotNull(actor, nameof(actor));

        if (!IsSubmitted)
        {
            throw new InvalidOperationException($"Submission {Id} cannot be re-referenced before it is submitted.");
        }

        ReferenceNumber = referenceNumber;

        Touch(actor, utcNow);
    }

    // Freezes the rendered report content captured at submit time so the downloadable "copy for your records" never
    // drifts when the underlying activities/forms are edited afterwards. Stored and read back as opaque JSON.
    public void AttachReportSnapshot(string? snapshotJson)
    {
        ReportSnapshotJson = snapshotJson;
    }

    // "Confirm your activities for this quarter" — unlocks the report sections and (re)clears every review flag
    // raised by activity-list changes since the previous confirmation.
    public void ConfirmActivities(UserId actor, DateTime utcNow)
    {
        Guard.NotNull(actor, nameof(actor));

        ActivitiesConfirmedAt = utcNow;
        ActivitiesConfirmedBy = actor;
        ActivitiesReviewRequired = false;

        foreach (var section in _sections)
        {
            section.ClearReviewFlag(utcNow);
        }

        Touch(actor, utcNow);
    }

    // The LA's activity list changed (create / update / delete / restore) after it was confirmed: every section the
    // user has already started must be reviewed and reconfirmed. No-op before the first confirmation.
    public void FlagActivityListChanged(DateTime utcNow)
    {
        // No-op before the first confirmation, and once submitted — a finalised report must not be reopened for
        // review by later activity-list edits.
        if (!AreActivitiesConfirmed || IsSubmitted)
        {
            return;
        }

        ActivitiesReviewRequired = true;

        foreach (var section in _sections.Where(s => !s.Status.IsNotStarted))
        {
            section.FlagForReview(utcNow);
        }

        UpdatedAt = utcNow;
    }

    public void SetSectionStatus(string moduleCode, SubmissionSectionStatus status, UserId actor, DateTime utcNow)
    {
        Guard.NotNullOrEmptyOrWhiteSpace(moduleCode, nameof(moduleCode));
        Guard.NotNull(status, nameof(status));
        Guard.NotNull(actor, nameof(actor));

        var section = _sections.FirstOrDefault(s => s.ModuleCode == moduleCode)
            ?? throw new SubmissionSectionNotFoundException(Id, moduleCode);

        section.SetStatus(status, utcNow);

        // Re-completing a flagged section counts as reviewing it; once nothing is left to review the task-list
        // banner goes too (reconfirming the activities list is the other path that clears everything).
        if (status.IsCompleted)
        {
            section.ClearReviewFlag(utcNow);
        }

        if (_sections.All(s => !s.ReviewRequired))
        {
            ActivitiesReviewRequired = false;
        }

        Touch(actor, utcNow);
    }

    private void Touch(UserId actor, DateTime utcNow)
    {
        UpdatedBy = actor;
        UpdatedAt = utcNow;
    }
}

public class SubmissionSectionNotFoundException(SubmissionId id, string moduleCode)
    : Exception($"Submission {id} has no section for module \"{moduleCode}\".");