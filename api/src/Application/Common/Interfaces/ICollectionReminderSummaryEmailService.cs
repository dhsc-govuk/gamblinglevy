using LanguageExt;

namespace Application.Common.Interfaces;

// Tells DHSC Super Admins that a deadline reminder has gone out to the data providers who have not yet submitted
// (BLUM-7571 AC5/AC6).
//
// Same best-effort contract as ICollectionReminderEmailService, and the same EmailNotConfiguredException
// discriminator on the Left.
//
// AC6 asks the email to point at where submission status can be reviewed. Settings.Notify.ClientUrl is the
// sign-in page and is passed through unchanged (see NotifyCollectionClosedEmailService for why no path is
// appended); once signed in, the per-collection status list lives at /admin/reports/{collectionId}. That
// navigation belongs in the Notify template's copy, which is the point of managing wording in Notify.
public interface ICollectionReminderSummaryEmailService
{
    Task<Either<Exception, Unit>> SendReminderSummaryAsync(
        string email,
        CollectionReminderSummary summary,
        CancellationToken cancellationToken);
}

// OutstandingOrganisationCount is what makes AC6's "confirms a reminder has been sent to outstanding local
// authorities" honest — including when it is zero. AC5 is unconditional ("GIVEN the reminder trigger point is
// reached, THEN an email is also automatically sent"), so the summary goes out even when everybody has already
// submitted, and the template needs a branch for that case. RecipientName, PeriodLabel and the reporting period
// dates match the live Notify template's actual personalisation fields — see CollectionReminderNotification for
// why this moved off the machine-shaped names it was first guessed against before the template existed.
//
// IsMaturityAssessment drives the template's own quarterly/maturity conditional content — the template has no
// "else", so the email service sends is_quarterly_report and is_maturity_assessment as two independent,
// mutually exclusive personalisation values rather than one the template would have to negate.
public record CollectionReminderSummary(
    string CollectionName,
    string RecipientName,
    string PeriodLabel,
    DateOnly ReportingPeriodStart,
    DateOnly ReportingPeriodEnd,
    DateOnly SubmissionDeadline,
    int OutstandingOrganisationCount,
    bool IsMaturityAssessment);