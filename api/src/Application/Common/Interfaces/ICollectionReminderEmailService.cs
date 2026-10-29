using LanguageExt;

namespace Application.Common.Interfaces;

// Reminds a data provider that it has not yet submitted for an open data collection (BLUM-7571).
//
// Best-effort, exactly like ICollectionClosedEmailService: a failed send is logged and swallowed. It must never
// fail the manual-reminder request that triggered it, and must never stop the background sweep moving on.
//
// A Left of EmailNotConfiguredException (declared alongside ICollectionClosedEmailService) means the send was
// refused before it was attempted because Notify has no template for this purpose yet — CollectionReminderNotifier
// handles that in the opposite way to a send that was attempted and failed, and the TYPE is the only thing that
// tells it which it has.
public interface ICollectionReminderEmailService
{
    Task<Either<Exception, Unit>> SendReminderAsync(
        string email,
        CollectionReminderNotification notification,
        CancellationToken cancellationToken);
}

// AC2: the collection name, the reporting period and the deadline date, all stated plainly, matched to the live
// Notify template's actual personalisation fields rather than the machine-shaped names this was first guessed
// against before the template existed. RecipientName, not OrganisationName: the template greets the person
// ("Dear ((Name))") and disambiguates the report by PeriodLabel/CollectionName rather than by organisation.
// Deliberately no "days remaining": nothing in the acceptance criteria needs it, and on a catch-up send (a fire
// date the service was asleep for) it would be stale or negative.
public record CollectionReminderNotification(
    string CollectionName,
    string RecipientName,
    string PeriodLabel,
    DateOnly ReportingPeriodStart,
    DateOnly ReportingPeriodEnd,
    DateOnly SubmissionDeadline);