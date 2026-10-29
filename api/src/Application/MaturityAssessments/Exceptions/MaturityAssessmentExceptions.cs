using Application.Forms.Answers;
using Domain.Collections;
using Domain.MaturityAssessments;

namespace Application.MaturityAssessments.Exceptions;

public abstract class MaturityAssessmentException(MaturityAssessmentId id, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public MaturityAssessmentId AssessmentId { get; } = id;
}

// Collection missing, not a maturity assessment, still a draft, or not assigned to the caller's organisation.
// Maps to 404 — assignment is not leaked to organisations outside the collection (the quarterly precedent).
public class MaturityAssessmentCollectionNotEligibleException(CollectionId collectionId)
    : MaturityAssessmentException(MaturityAssessmentId.Empty(), $"Collection {collectionId} is not available for your organisation.");

// Mapped to 422 REPORT_NOT_OPEN — the same envelope the quarterly flow uses, so the client's mapReportError
// handles it unchanged.
public class MaturityAssessmentNotOpenException(CollectionId collectionId, string currentStatus)
    : MaturityAssessmentException(MaturityAssessmentId.Empty(), $"Collection {collectionId} is {currentStatus} and is not accepting maturity assessments.")
{
    public string CurrentStatus { get; } = currentStatus;
}

public class MaturityAssessmentAlreadyExistsException(MaturityAssessmentId id)
    : MaturityAssessmentException(id, "A maturity assessment for this collection has already been started by your organisation.");

public class MaturityAssessmentDoesNotExistException(CollectionId collectionId)
    : MaturityAssessmentException(MaturityAssessmentId.Empty(), $"No maturity assessment has been started for collection {collectionId} by your organisation.");

public class MaturityAssessmentSectionUnknownException(MaturityAssessmentId id, string sectionCode)
    : MaturityAssessmentException(id, $"The maturity assessment has no section \"{sectionCode}\".");

public class MaturityAssessmentPageUnknownException(MaturityAssessmentId id, string sectionCode, string pageCode)
    : MaturityAssessmentException(id, $"Section \"{sectionCode}\" of the maturity assessment has no page \"{pageCode}\".");

// The assessment has already been submitted and is read-only — applies to a re-submit and to any later edit
// attempt. Mapped to 409 Conflict.
public class MaturityAssessmentAlreadySubmittedException(MaturityAssessmentId id)
    : MaturityAssessmentException(id, "This maturity assessment has already been submitted and can no longer be changed.");

// The assessment cannot be submitted yet: a section is still incomplete. Mapped to 422 REPORT_INCOMPLETE.
public class MaturityAssessmentIncompleteException(MaturityAssessmentId id)
    : MaturityAssessmentException(id, "Complete every section before you submit this maturity assessment.");

// One or more answers are missing (on complete / non-draft save) or invalid. Carries per-field errors keyed by
// question code. Mapped to the shared SUBMISSION_ANSWERS_INVALID envelope so the client routes each error to its
// field exactly as it does for the quarterly sections.
public class MaturityAnswersInvalidException(MaturityAssessmentId id, IReadOnlyList<AnswerValidationError> errors)
    : MaturityAssessmentException(id, "The maturity assessment has invalid or missing answers.")
{
    public IReadOnlyList<AnswerValidationError> Errors { get; } = errors;
}

// A concurrent submit in the same financial year took the same reference-number sequence and the unique index
// rejected ours. The submit handler retries with the next sequence; this only surfaces (mapped to a retryable 409)
// if every retry attempt collides.
public class MaturityAssessmentReferenceConflictException(MaturityAssessmentId id)
    : MaturityAssessmentException(id, "Your maturity assessment could not be submitted because of a temporary conflict. Please try again.");

public class MaturityAssessmentForbiddenException(MaturityAssessmentId id, string message)
    : MaturityAssessmentException(id, message);

public class MaturityAssessmentUnknownException(MaturityAssessmentId id, Exception innerException)
    : MaturityAssessmentException(id, $"Unknown exception for the maturity assessment {id}", innerException);