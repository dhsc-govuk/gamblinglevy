using Domain.Collections;

namespace Application.Common.Interfaces;

// Sends the BLUM-7571 deadline reminders: one email per contact at every assigned organisation that has not yet
// submitted, plus a summary to every active DHSC Super Admin.
//
// Two entry points onto one shared dispatch path, mirroring ICollectionClosureNotifier. They exist for the same
// reason: a configured reminder's fire date arriving is not an event anything observes — CollectionStatus is
// derived and collection_reminders holds an offset, not a date — so something has to go looking, while the manual
// button (AC1's "or a manual reminder is sent") has a caller and a request behind it. Sharing the dispatch path is
// what makes AC3's already-submitted filter hold on both by construction rather than by two implementations
// happening to agree.
public interface ICollectionReminderNotifier
{
    // Every open collection with a due, unclaimed trigger point. Returns how many collections were dispatched for.
    Task<int> SweepAsync(CancellationToken cancellationToken = default);

    // Dispatches immediately for one already-loaded, open collection, whatever the configured cadence says.
    // Recorded with trigger type "manual", which the dispatch table's unique index deliberately excludes so an
    // admin may send more than one in a day.
    Task<CollectionReminderResult> SendManualAsync(Collection collection, CancellationToken cancellationToken = default);
}

public readonly record struct CollectionReminderResult(
    int OutstandingOrganisations,
    int ProviderEmailsSent,
    int DhscEmailsSent);