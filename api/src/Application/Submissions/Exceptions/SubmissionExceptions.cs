using Application.Forms.Answers;
using Application.Submissions.Offline;
using Domain.Activities;
using Domain.Collections;
using Domain.Submissions;

namespace Application.Submissions.Exceptions;

public abstract class SubmissionException(SubmissionId id, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public SubmissionId SubmissionId { get; } = id;
}

// Collection missing, not quarterly, or not assigned to the caller's organisation. Maps to 404 — assignment is not
// leaked to organisations outside the collection.
public class SubmissionCollectionNotEligibleException(CollectionId collectionId)
    : SubmissionException(SubmissionId.Empty(), $"Collection {collectionId} is not available for your organisation.");

public class SubmissionCollectionNotOpenException(CollectionId collectionId, string currentStatus)
    : SubmissionException(SubmissionId.Empty(), $"Collection {collectionId} is {currentStatus} and is not accepting submissions.")
{
    public string CurrentStatus { get; } = currentStatus;
}

public class SubmissionAlreadyExistsException(SubmissionId id)
    : SubmissionException(id, "A report for this collection has already been started by your organisation.");

public class SubmissionDoesNotExistException(CollectionId collectionId)
    : SubmissionException(SubmissionId.Empty(), $"No report has been started for collection {collectionId} by your organisation.");

public class SubmissionActivitiesNotConfirmedException(SubmissionId id)
    : SubmissionException(id, "Confirm your list of activities before entering report data.");

// The report cannot be submitted yet: a section is still incomplete, or a section needs review after an activity-list
// change. Mapped to 422 REPORT_INCOMPLETE.
public class SubmissionNotReadyToSubmitException(SubmissionId id)
    : SubmissionException(id, "Complete every section before you submit this report.");

// The report has already been submitted and is read-only — applies to a re-submit and to any later edit attempt.
// Mapped to 409 Conflict.
public class SubmissionAlreadySubmittedException(SubmissionId id)
    : SubmissionException(id, "This report has already been submitted and can no longer be changed.");

// A submission that was never finalised has no frozen ReportSnapshotJson to regenerate — there is nothing to
// rebuild until the LA actually submits. Mapped to 422, mirroring SubmissionNotReadyToSubmitException.
public class SubmissionNotSubmittedException(SubmissionId id)
    : SubmissionException(id, "This report has not been submitted yet, so it has no snapshot to regenerate.");

public class SubmissionActivityNotReportableException(SubmissionId id, ActivityId activityId, string reason)
    : SubmissionException(id, $"Activity {activityId} cannot be reported on: {reason}")
{
    public ActivityId ActivityId { get; } = activityId;
}

public class SubmissionAnswersInvalidException(SubmissionId id, ActivityId activityId, IReadOnlyList<AnswerValidationError> errors)
    : SubmissionException(id, "The report has invalid answers to one or more questions.")
{
    public ActivityId ActivityId { get; } = activityId;

    public IReadOnlyList<AnswerValidationError> Errors { get; } = errors;
}

// One or more "Record funding spent" amounts are missing (on complete) or invalid. Carries per-field errors keyed
// like actual-{activityId} / other-spend / forecast-{activityId}-q{n} / other-forecast-q{n} so the frontend can
// route each error back to its owning screen. Mapped to the shared SUBMISSION_ANSWERS_INVALID envelope.
public class FundingSpendInvalidException(SubmissionId id, IReadOnlyList<AnswerValidationError> errors)
    : SubmissionException(id, "The funding spend has invalid or missing amounts.")
{
    public IReadOnlyList<AnswerValidationError> Errors { get; } = errors;
}

// One or more "Provide strategic feedback" answers are missing (on complete / non-draft save) or invalid. Carries
// per-field errors keyed by question code. Mapped to the shared SUBMISSION_ANSWERS_INVALID envelope, like funding.
public class StrategicFeedbackInvalidException(SubmissionId id, IReadOnlyList<AnswerValidationError> errors)
    : SubmissionException(id, "The strategic feedback has invalid or missing answers.")
{
    public IReadOnlyList<AnswerValidationError> Errors { get; } = errors;
}

// A concurrent submit in the same financial year + quarter took the same reference-number sequence and the unique
// index rejected ours. The submit handler retries with the next sequence; this only surfaces (mapped to a retryable
// 409 Conflict) if every retry attempt collides — a transient state the LA can recover from by trying again.
public class SubmissionReferenceConflictException(SubmissionId id)
    : SubmissionException(id, "Your report could not be submitted because of a temporary conflict. Please try again.");

public class SubmissionForbiddenException(SubmissionId id, string message)
    : SubmissionException(id, message);

public class SubmissionUnknownException(SubmissionId id, Exception innerException)
    : SubmissionException(id, $"Unknown exception for the submission {id}", innerException);

// An offline report upload (BLUM-8310) refused as a whole before any answer is read: no file, not a CSV, not one of
// our templates, a template for another report or an out-of-date one. Code is the OFFLINE_* envelope code and
// Message is the fixed GDS copy the page shows (never built from the file's content). Mapped to 400.
public class OfflineUploadRejectedException(SubmissionId id, string code, string message)
    : SubmissionException(id, message)
{
    public string Code { get; } = code;
}

// An offline report upload whose answers have problems. Nothing is saved; the page lists Errors and offers
// AnnotatedFile — the person's own file with each row's problems written into its Errors column. Mapped to 422
// OFFLINE_UPLOAD_INVALID.
public class OfflineUploadInvalidException(
    SubmissionId id,
    string moduleCode,
    IReadOnlyList<OfflineCellError> errors,
    byte[] annotatedFile,
    string annotatedFileName)
    : SubmissionException(id, "The selected file has errors")
{
    public string ModuleCode { get; } = moduleCode;

    public IReadOnlyList<OfflineCellError> Errors { get; } = errors;

    public byte[] AnnotatedFile { get; } = annotatedFile;

    public string AnnotatedFileName { get; } = annotatedFileName;
}

// An offline template asked for a section this report does not have. Mapped to 404.
public class OfflineSectionNotInReportException(SubmissionId id, string moduleCode)
    : SubmissionException(id, $"This report has no {moduleCode} section.");