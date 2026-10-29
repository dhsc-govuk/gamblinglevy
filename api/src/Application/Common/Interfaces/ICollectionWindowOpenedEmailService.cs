using LanguageExt;

namespace Application.Common.Interfaces;

// Tells a data provider that a reporting window has opened and what it has to report on (BLUM-7721 AC1/AC3).
//
// Same best-effort contract as ICollectionReminderEmailService: a failed send is logged and swallowed, and must
// never stop the background sweep moving on to the next organisation or the next collection.
//
// A Left of EmailNotConfiguredException (declared alongside ICollectionClosedEmailService) means the send was
// refused before it was attempted because Notify has no template for this purpose yet. CollectionReminderNotifier
// handles that in the opposite way to a send that was attempted and failed, and the TYPE is the only thing that
// tells it which it has.
public interface ICollectionWindowOpenedEmailService
{
    Task<Either<Exception, Unit>> SendWindowOpenedAsync(
        string email,
        CollectionWindowOpenedNotification notification,
        CancellationToken cancellationToken);
}

// AC3: the quarter, the reporting period's start and end, the deadline, and the report's name.
//
// PeriodLabel rather than a nullable int Quarter, because AC4's "quarter (or maturity assessment period)" is one
// slot in the template with two possible vocabularies, and deciding which belongs in the Application layer with
// the rest of the business vocabulary — the same split as SubmissionConfirmation.QuarterLabel. The Notify service
// then owns only presentation: date formatting and the sign-in URL.
//
// The reporting period is what the report COVERS; the deadline is when the window shuts. They are different dates
// (they coincide only when Collection.WindowSameAsReportingPeriod is true), which is why both are carried — the
// same distinction ICollectionClosedEmailService documents.
//
// RecipientName, not OrganisationName, matching CollectionReminderNotification: the BLUM-8094 template greets the
// person ("Dear ((Name))") and never mentions the organisation, so there is nothing for OrganisationName to bind
// to and it was replaced rather than kept alongside.
public record CollectionWindowOpenedNotification(
    string CollectionName,
    string RecipientName,
    string PeriodLabel,
    DateOnly ReportingPeriodStart,
    DateOnly ReportingPeriodEnd,
    DateOnly SubmissionDeadline);