using Domain.MaturityAssessments;

namespace Application.Common.Interfaces.Repositories;

public interface IMaturityAssessmentRepository
{
    Task<MaturityAssessment> AddAsync(MaturityAssessment assessment, CancellationToken cancellationToken = default);

    // Inserts the assessment, answering false — with nothing left tracked — when another request has already created
    // one for the same (collection, organisation), so the Start handler can answer 409 instead of 500 for the loser
    // of a race the task list's auto-start makes likely (two tabs, two colleagues).
    Task<bool> TryAddAsync(MaturityAssessment assessment, CancellationToken cancellationToken = default);
    Task UpdateAsync(MaturityAssessment assessment, CancellationToken cancellationToken = default);

    // Saves a tracked assessment and translates a unique-violation on the reference-number index into a
    // MaturityAssessmentReferenceConflictException so the submit handler can retry with the next sequence.
    Task UpdateDetectingReferenceConflictAsync(MaturityAssessment assessment, CancellationToken cancellationToken = default);
}