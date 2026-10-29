using LanguageExt;

namespace Application.Common.Interfaces;

// Tells DHSC Super Admins that a data-collection window has closed and the consolidated report is ready to
// download (BLUM-7719).
//
// Best-effort, exactly like ISubmissionConfirmationEmailService: the window has closed whether or not the mail
// goes out, so a failed send is logged and swallowed. It must never fail the close request that triggered it,
// and must never stop the background sweep moving on to the next collection.
public interface ICollectionClosedEmailService
{
    Task<Either<Exception, Unit>> SendClosureAsync(
        string email,
        CollectionClosureNotification notification,
        CancellationToken cancellationToken);
}

// Returned as the Left when a send was refused before it was attempted, because GOV.UK Notify is not configured
// for this purpose — as opposed to a send that was attempted and failed. CollectionClosureNotifier has to handle
// the two in opposite ways and cannot tell them apart otherwise:
//
//   * a send that failed is still stamped as notified, because the marker is per collection, not per recipient,
//     and retrying it would re-mail every admin who DID receive it;
//   * an unconfigured template must leave the marker clear, because that is the state this feature ships in
//     (Notify.CollectionClosedTemplateId is empty until DHSC creates the template) and stamping there would mark
//     every window that closes in the meantime as notified, forever.
//
// A type rather than a message string on purpose: this is the same class of contract as the envelope constants in
// CLAUDE.md, and rewording the message must not silently flip which branch a refusal takes.
public class EmailNotConfiguredException(string message) : Exception(message);

// AC2 wants the REPORTING PERIOD (what the report covers), not the collection window (when LAs could submit).
// The two coincide only when Collection.WindowSameAsReportingPeriod is true, so they are not interchangeable.
//
// RecipientName, matching CollectionReminderSummary/CollectionWindowOpenedSummary: the BLUM-8095 template greets
// the admin by name ("Dear ((name))").
public record CollectionClosureNotification(
    string CollectionName,
    string RecipientName,
    DateOnly ReportingPeriodStart,
    DateOnly ReportingPeriodEnd);