using Domain.Collections;

namespace Application.Common.Interfaces.Repositories;

public interface ICollectionRepository
{
    Task<Collection> AddAsync(Collection collection, CancellationToken cancellationToken = default);
    Task UpdateAsync(Collection collection, CancellationToken cancellationToken = default);

    // BLUM-9084: row locks held until the open transaction commits (ITransactionRunner.RunAsync), and refused outside
    // one. A change to these collections' forms or recorded versions that depends on no report having started takes
    // LockForFormChangesAsync before it checks; starting a report takes LockForReportStartAsync before it reads the
    // collection. The two wait for each other, so the check stays true until the change commits; report starts never
    // wait for each other. See Infrastructure's CollectionRowLocks.
    Task LockForFormChangesAsync(IReadOnlyCollection<CollectionId> collectionIds, CancellationToken cancellationToken = default);

    Task LockForReportStartAsync(CollectionId collectionId, CancellationToken cancellationToken = default);

    // Atomically claims the BLUM-8155 "window closed" LA notification for one collection: flips
    // WindowClosedLaNotifiedAt from NULL to the instant the caller passes, in a single UPDATE, so two sweep
    // instances racing during a deploy — or a sweep that resumes after a restart mid-loop — cannot both decide
    // the same collection is still owed a send. Returns true only for the caller that won the race (updated
    // exactly one row); false means another instance already claimed it (or already sent it).
    Task<bool> TryClaimWindowClosedLaNotificationAsync(CollectionId collectionId, DateTime utcNow, CancellationToken cancellationToken = default);

    // Releases a claim taken above when nothing could actually be sent — Notify not configured yet, or the sweep
    // was cancelled before any email went out — so the next sweep retries instead of skipping the collection
    // forever.
    Task ReleaseWindowClosedLaNotificationClaimAsync(CollectionId collectionId, CancellationToken cancellationToken = default);
}