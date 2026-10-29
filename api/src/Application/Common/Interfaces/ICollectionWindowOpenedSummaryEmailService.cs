using LanguageExt;

namespace Application.Common.Interfaces;

// Tells DHSC Super Admins that a reporting window has opened (BLUM-7721 AC2/AC4).
//
// Same best-effort contract and the same EmailNotConfiguredException discriminator on the Left as
// ICollectionWindowOpenedEmailService.
public interface ICollectionWindowOpenedSummaryEmailService
{
    Task<Either<Exception, Unit>> SendWindowOpenedSummaryAsync(
        string email,
        CollectionWindowOpenedSummary summary,
        CancellationToken cancellationToken);
}

// AC4: the data collection name, the quarter (or maturity assessment period), the reporting period's start and
// end, and the deadline.
//
// AssignedOrganisationCount is not in the acceptance criteria, but it is the analogue of BLUM-7571's
// outstanding_count and the only thing that lets the template say how many organisations were told — which is
// what makes this a summary rather than a copy of the provider email. It can legitimately be 0 (a window with no
// organisations assigned yet), and the template needs a branch for that.
//
// RecipientName, matching CollectionReminderSummary: the BLUM-8094 template greets the admin by name.
//
// IsMaturityAssessment, matching CollectionReminderSummary/NotifyCollectionReminderSummaryEmailService: the
// window-opened summary template has the same quarterly-vs-maturity-assessment branch (with the reporting period
// dates inline in both branches), which GOV.UK Notify's ((condition??text)) syntax cannot express in one shared
// template — so this selects between CollectionWindowOpenedSummaryTemplateId and
// CollectionWindowOpenedSummaryMaturityTemplateId rather than driving a conditional inside a single template.
public record CollectionWindowOpenedSummary(
    string CollectionName,
    string RecipientName,
    string PeriodLabel,
    DateOnly ReportingPeriodStart,
    DateOnly ReportingPeriodEnd,
    DateOnly SubmissionDeadline,
    int AssignedOrganisationCount,
    bool IsMaturityAssessment);