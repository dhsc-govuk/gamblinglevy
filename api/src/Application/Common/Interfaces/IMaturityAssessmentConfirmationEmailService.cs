using LanguageExt;

namespace Application.Common.Interfaces;

// Sends the "your maturity assessment has been submitted" confirmation to the submitting LA user (BLUM-7858 AC4).
// Best-effort, exactly like ISubmissionConfirmationEmailService: the submit is already persisted and irreversible,
// so a failed send is logged and never rolls it back.
public interface IMaturityAssessmentConfirmationEmailService
{
    Task<Either<Exception, Unit>> SendConfirmationAsync(
        string email,
        MaturityAssessmentConfirmation confirmation,
        CancellationToken cancellationToken);
}

// Everything the confirmation email needs, already composed by the handler. PeriodLabel is CollectionPeriodLabel's
// "Maturity assessment 2026/27".
public record MaturityAssessmentConfirmation(
    Guid CollectionId,
    string ReferenceNumber,
    DateTime SubmittedAt,
    string PeriodLabel,
    string CollectionName,
    string LocalAuthorityName);