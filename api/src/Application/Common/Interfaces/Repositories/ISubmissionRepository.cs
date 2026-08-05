using Domain.Submissions;

namespace Application.Common.Interfaces.Repositories;

public interface ISubmissionRepository
{
    Task<Submission> AddAsync(Submission submission, CancellationToken cancellationToken = default);
    Task UpdateAsync(Submission submission, CancellationToken cancellationToken = default);

    // Saves a tracked submission and translates a unique-violation on the reference-number index into a
    // SubmissionReferenceConflictException so the submit handler can retry with the next sequence (rather than the
    // generic RepositoryBase.UpdateAsync, which wraps every DbUpdateException into an opaque 500).
    Task UpdateDetectingReferenceConflictAsync(Submission submission, CancellationToken cancellationToken = default);
}