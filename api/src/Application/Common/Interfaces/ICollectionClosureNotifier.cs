using Domain.Collections;

namespace Application.Common.Interfaces;

// Sends the BLUM-7719 closure notification and records that it has been sent.
//
// There are two ways a window closes and only one of them is a command. CloseCollectionCommand sets
// IsManuallyClosed; the far more common path is the submission deadline simply passing, where nothing runs at
// all and Collection.GetStatus() just starts answering Closed. So this has two entry points onto one shared
// send-and-stamp path: NotifyAsync for the manual close, SweepAsync for everything that closed on its own.
//
// Collection.ClosureNotifiedAt is what makes both idempotent — without it the sweep would re-send on every run.
public interface ICollectionClosureNotifier
{
    // Notifies every collection that has closed and not yet been notified. Returns how many were notified.
    Task<int> SweepAsync(CancellationToken cancellationToken = default);

    // Notifies for one already-loaded, already-closed collection. A no-op if it has been notified already.
    Task NotifyAsync(Collection collection, CancellationToken cancellationToken = default);
}