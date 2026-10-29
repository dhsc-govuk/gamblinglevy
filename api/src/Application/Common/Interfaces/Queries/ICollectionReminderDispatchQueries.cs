using Domain.Collections;

namespace Application.Common.Interfaces.Queries;

public interface ICollectionReminderDispatchQueries
{
    // Every SCHEDULED trigger point already claimed for the given collections, from notBefore onwards, in ONE
    // read. Lets the sweep skip work it has already done instead of attempting an insert and catching the
    // unique-index violation once per collection on every run. The index is still the authority — this is an
    // optimisation, not the guard.
    //
    // Filtered to trigger_type = 'scheduled' rather than <> 'manual'. Those were the same set until BLUM-7721
    // added 'window_opened', and they are not any more: the unique index keys on (collection, trigger, date), so
    // a window-opened claim and a scheduled reminder legitimately share a date whenever a configured "0 days
    // after the collection opens" reminder resolves onto the open date. A "not manual" predicate would let
    // either one convince the sweep the other had already gone out.
    Task<IReadOnlyList<ClaimedReminderDispatch>> GetClaimedScheduledAsync(
        IReadOnlyCollection<CollectionId> collectionIds,
        DateOnly notBefore,
        CancellationToken cancellationToken = default);

    // The subset of the given collections that have already had their window-open notification claimed
    // (BLUM-7721), whenever that was.
    //
    // Deliberately NOT date-keyed, and deliberately not floored by notBefore. "The reporting window has opened"
    // is a fact about a collection, announced once. CollectionOpenDate is mutable on a live window —
    // UpdateCollectionCommand even recomputes it from the financial year and quarter when
    // WindowSameAsReportingPeriod is true — so a date-keyed claim would let an admin correcting an unrelated
    // field re-announce the opening to every Local Authority in the country. The partial unique index
    // ix_collection_reminder_dispatches_window_opened is the authority; this is the same read-ahead optimisation
    // as GetClaimedScheduledAsync.
    Task<IReadOnlyList<Guid>> GetCollectionsWithWindowOpenedClaimAsync(
        IReadOnlyCollection<CollectionId> collectionIds,
        CancellationToken cancellationToken = default);
}

public readonly record struct ClaimedReminderDispatch(Guid CollectionId, DateOnly ScheduledFor);