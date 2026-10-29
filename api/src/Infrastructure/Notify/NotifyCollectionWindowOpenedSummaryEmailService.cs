using System.Globalization;
using Application.Common.Interfaces;
using Application.Common.Settings;
using LanguageExt;
using Microsoft.Extensions.Logging;
using Notify.Interfaces;

namespace Infrastructure.Notify;

// The BLUM-7721 summary telling DHSC Super Admins that a reporting window has opened (AC2/AC4). Registered
// unconditionally for the same reason as its sibling — Notify-only, no SendGrid twin.
public class NotifyCollectionWindowOpenedSummaryEmailService : NotifyEmailServiceBase, ICollectionWindowOpenedSummaryEmailService
{
    private static readonly CultureInfo EnGb = CultureInfo.GetCultureInfo("en-GB");

    public NotifyCollectionWindowOpenedSummaryEmailService(
        Lazy<IAsyncNotificationClient> notificationClient,
        ApplicationSettings settings,
        ILogger<NotifyCollectionWindowOpenedSummaryEmailService> logger)
        : base(notificationClient, settings, logger)
    {
        // No constructor guard — see NotifyCollectionWindowOpenedEmailService.
    }

    public Task<Either<Exception, Unit>> SendWindowOpenedSummaryAsync(
        string email,
        CollectionWindowOpenedSummary summary,
        CancellationToken cancellationToken)
    {
        // Two templates, not one with a quarterly/maturity conditional branch — same reason as
        // NotifyCollectionReminderSummaryEmailService: GOV.UK Notify's ((condition??text)) syntax cannot contain
        // another placeholder, which rules out a shared template whose branches both need the reporting period
        // dates inline.
        var templateId = summary.IsMaturityAssessment
            ? Settings.CollectionWindowOpenedSummaryMaturityTemplateId
            : Settings.CollectionWindowOpenedSummaryTemplateId;

        if (string.IsNullOrEmpty(Settings.ApiKey)
            || string.IsNullOrEmpty(templateId)
            || string.IsNullOrEmpty(Settings.ClientUrl))
        {
            return Task.FromResult<Either<Exception, Unit>>(
                new EmailNotConfiguredException("Notify is not configured (ApiKey/CollectionWindowOpenedSummaryTemplateId or CollectionWindowOpenedSummaryMaturityTemplateId/ClientUrl missing); collection window-opened summary not sent."));
        }

        // Key names match the BLUM-8094 template copy, not the machine-shaped names ("collection_name" etc.) this
        // send used before that copy existed — same transition NotifyCollectionReminderSummaryEmailService already
        // made. Verify these against the live template's "Missing personalisation" 400s once
        // Notify__CollectionWindowOpenedSummaryTemplateId points at a real template.
        //
        // assigned_count has no placeholder in the BLUM-8094 draft copy — it is carried anyway (Notify ignores an
        // unused personalisation key) so nothing needs to change here if the template is revised to use it.
        var personalisation = new Dictionary<string, dynamic>
        {
            { "Name", summary.RecipientName },
            { "quarter X", summary.PeriodLabel },
            { "Start date", summary.ReportingPeriodStart.ToString("d MMMM yyyy", EnGb) },
            { "End date", summary.ReportingPeriodEnd.ToString("d MMMM yyyy", EnGb) },
            { "Deadline", summary.SubmissionDeadline.ToString("d MMMM yyyy", EnGb) },
            { "Data Collection", summary.CollectionName },
            { "assigned_count", summary.AssignedOrganisationCount.ToString(EnGb) },
            { "login_url", Settings.ClientUrl },
        };

        return SendAsync(email, templateId, personalisation, "collection window opened summary", cancellationToken);
    }
}