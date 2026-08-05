using Domain.Collections;
using Domain.Submissions;

namespace Application.Common.Interfaces.Repositories;

public interface ISubmissionSpendEntryRepository
{
    Task<IReadOnlyList<SubmissionSpendEntry>> GetForSubmissionAsync(SubmissionId submissionId, CancellationToken cancellationToken = default);

    // Replaces one kind (Actual or Forecast) of the submission's spend wholesale (delete-then-insert) so saving the
    // "Activity spending" screen does not wipe the "Forecast spend" screen and vice-versa.
    Task ReplaceForSubmissionAsync(SubmissionId submissionId, SpendEntryKind kind, IReadOnlyList<SubmissionSpendEntry> entries, CancellationToken cancellationToken = default);

    // Sum of Actual spend across the given submissions — feeds the funding dashboard's cumulative spend.
    Task<decimal> SumActualForSubmissionsAsync(IReadOnlyList<SubmissionId> submissionIds, CancellationToken cancellationToken = default);

    // Total Actual (this-quarter) spend per organisation for every SUBMITTED submission against a collection — the
    // "total reported spend" column of the DHSC submission export. Draft (unsubmitted) spend is excluded; an
    // organisation with no submitted submission is absent (the export defaults it to 0).
    Task<IReadOnlyDictionary<Guid, decimal>> SumActualByOrganisationForCollectionAsync(CollectionId collectionId, CancellationToken cancellationToken = default);
}