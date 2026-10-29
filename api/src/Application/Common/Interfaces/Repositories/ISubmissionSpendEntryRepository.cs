using Domain.Collections;
using Domain.Organisations;
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

    // Every ACTIVITY-attributed Actual spend row the organisation has ever recorded, each carrying the financial year
    // of the collection it was reported against. One query for all years: the per-activity "Total spent so far" inset
    // carries spend forward between financial years (BLUM-7703), and fetching each earlier submission's entries in
    // turn made that a per-submission round trip bounded only by how long the organisation had been reporting.
    // The "Other" (null activity) line is excluded — the caller keys by activity.
    Task<IReadOnlyList<OrganisationActivitySpendRow>> GetActualActivitySpendByFinancialYearAsync(OrganisationId organisationId, CancellationToken cancellationToken = default);

    // Total Actual (this-quarter) spend per organisation for every SUBMITTED submission against a collection — the
    // "total reported spend" column of the DHSC submission export. Draft (unsubmitted) spend is excluded; an
    // organisation with no submitted submission is absent (the export defaults it to 0).
    Task<IReadOnlyDictionary<Guid, decimal>> SumActualByOrganisationForCollectionAsync(CollectionId collectionId, CancellationToken cancellationToken = default);
}

// One activity-attributed Actual spend row with the financial year of the collection it was reported against.
// FinancialYear is nullable because a collection carries it optionally; a caller ordering years must parse it
// through FinancialYearRange.TryParseStartYear rather than comparing the strings.
public readonly record struct OrganisationActivitySpendRow(Guid ActivityId, string? FinancialYear, decimal Amount);