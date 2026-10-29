using System.Globalization;
using Application.Common.Interfaces;
using Application.Common.Settings;
using LanguageExt;
using Microsoft.Extensions.Logging;
using Notify.Interfaces;

namespace Infrastructure.Notify;

// The BLUM-7571 summary telling DHSC Super Admins that a deadline reminder has gone out (AC5/AC6). Registered
// unconditionally for the same reason as its sibling — Notify-only, no SendGrid twin.
public class NotifyCollectionReminderSummaryEmailService : NotifyEmailServiceBase, ICollectionReminderSummaryEmailService
{
    private static readonly CultureInfo EnGb = CultureInfo.GetCultureInfo("en-GB");

    public NotifyCollectionReminderSummaryEmailService(
        Lazy<IAsyncNotificationClient> notificationClient,
        ApplicationSettings settings,
        ILogger<NotifyCollectionReminderSummaryEmailService> logger)
        : base(notificationClient, settings, logger)
    {
        // No constructor guard — see NotifyCollectionReminderEmailService.
    }

    public Task<Either<Exception, Unit>> SendReminderSummaryAsync(
        string email,
        CollectionReminderSummary summary,
        CancellationToken cancellationToken)
    {
        // Two templates, not one with a quarterly/maturity conditional branch: GOV.UK Notify's ((condition??text))
        // syntax cannot contain another placeholder — its regex forbids parentheses inside the conditional body —
        // which ruled out a shared template whose branches both need the reporting period dates inline.
        var templateId = summary.IsMaturityAssessment
            ? Settings.CollectionReminderSummaryMaturityTemplateId
            : Settings.CollectionReminderSummaryTemplateId;

        if (string.IsNullOrEmpty(Settings.ApiKey)
            || string.IsNullOrEmpty(templateId)
            || string.IsNullOrEmpty(Settings.ClientUrl))
        {
            return Task.FromResult<Either<Exception, Unit>>(
                new EmailNotConfiguredException("Notify is not configured (ApiKey/CollectionReminderSummaryTemplateId or CollectionReminderSummaryMaturityTemplateId/ClientUrl missing); collection reminder summary not sent."));
        }

        // Key names match the live Notify template's own personalisation fields, confirmed against its "Missing
        // personalisation" 400s — see NotifyCollectionReminderEmailService for why these differ from the
        // machine-shaped names this send used before the template existed. Note "name" here is lower-case, unlike
        // the sibling provider template's "Name" — the two templates were not authored with matching casing.
        //
        // outstanding_count is pre-formatted rather than passed as an int: the personalisation dictionary is
        // Dictionary<string, dynamic> and Notify would render either, but every other service here passes
        // strings and one consistent convention is cheaper than a surprise. It can be 0 — AC5 sends this summary
        // whenever the trigger point is reached, so the template must read sensibly when nobody was reminded.
        var personalisation = new Dictionary<string, dynamic>
        {
            { "Deadline", summary.SubmissionDeadline.ToString("d MMMM yyyy", EnGb) },
            { "name", summary.RecipientName },
            { "Data Collection", summary.CollectionName },
            { "quarter X", summary.PeriodLabel },
            { "Start date", summary.ReportingPeriodStart.ToString("d MMMM yyyy", EnGb) },
            { "End date", summary.ReportingPeriodEnd.ToString("d MMMM yyyy", EnGb) },
            { "outstanding_count", summary.OutstandingOrganisationCount.ToString(EnGb) },
            { "login_url", Settings.ClientUrl },
        };

        return SendAsync(email, templateId, personalisation, "collection reminder summary", cancellationToken);
    }
}