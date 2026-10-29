namespace Application.Common.Interfaces;

// Sends the BLUM-8155 "reporting window closed" notification to LA users of organisations that did not submit,
// and records that it has been sent. Sweep-only — unlike ICollectionClosureNotifier there is no manual-close
// entry point, because this fires a day after close rather than at the moment of closing.
//
// Collection.WindowClosedLaNotifiedAt is what makes the sweep idempotent — without it every run would re-send.
public interface ICollectionWindowClosedLaNotifier
{
    // Notifies every collection that closed at least a day ago and has not yet had its LA notification sent.
    // Returns how many were notified.
    Task<int> SweepAsync(CancellationToken cancellationToken = default);
}