using LanguageExt;

namespace Application.Common.Interfaces;

// Tells LA users of an organisation that did not submit its quarterly report that the reporting window has
// closed (BLUM-8155). Sent a day after close, to every active user of every organisation that was assigned to
// the collection but has no finalised submission — see OutstandingSubmissionResolver.
//
// Best-effort, exactly like ICollectionClosedEmailService: the window has closed whether or not the mail goes
// out, so a failed send is logged and swallowed. It must never stop the background sweep moving on.
//
// A Left of EmailNotConfiguredException (declared alongside ICollectionClosedEmailService) means the send was
// refused before it was attempted because Notify has no template for this purpose yet —
// CollectionWindowClosedLaNotifier handles that in the opposite way to a send that was attempted and failed, and
// the TYPE is the only thing that tells it which it has.
public interface ICollectionWindowClosedLaEmailService
{
    Task<Either<Exception, Unit>> SendWindowClosedAsync(
        string email,
        CollectionWindowClosedLaNotification notification,
        CancellationToken cancellationToken);
}

// CollectionName is sent to Notify under the key "Data collection" (NotifyCollectionWindowClosedLaEmailService),
// not "Quarterly report name" as the ticket's field list suggested — confirmed against the live template's own
// "Missing personalisation" 400. No sign-in link and no reporting-period dates — unlike
// CollectionReminderNotification, this email carries no call to action beyond the support contact address, which
// lives in the Notify template copy itself, not in personalisation.
public record CollectionWindowClosedLaNotification(
    string RecipientName,
    string CollectionName,
    DateOnly SubmissionDeadline);