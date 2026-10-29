using LanguageExt;

namespace Application.Common.Interfaces;

// Sends the "your quarterly report has been submitted" confirmation to the submitting LA user (BLUM-6954).
// Best-effort: the submit is already persisted and irreversible, so a failed send must never roll it back —
// callers log the Left and carry on. Mirrors IInvitationEmailService (dedicated service + own template).
public interface ISubmissionConfirmationEmailService
{
    Task<Either<Exception, Unit>> SendConfirmationAsync(
        string email,
        SubmissionConfirmation confirmation,
        CancellationToken cancellationToken);
}

// Everything the confirmation email needs, already composed by the handler. The service owns only presentation
// (ClientUrl + the confirmation-page route, date formatting, template substitution).
//
// RecipientName is the submitting user's own name (BLUM-7575): the Notify template greets them by name and does
// not use any of the other fields below — those remain for the SendGrid twin's richer HTML template.
public record SubmissionConfirmation(
    Guid CollectionId,
    string ReferenceNumber,
    DateTime SubmittedAt,
    string QuarterLabel,
    string CollectionName,
    string LocalAuthorityName,
    string RecipientName);