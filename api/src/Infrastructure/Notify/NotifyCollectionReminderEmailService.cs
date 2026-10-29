using System.Globalization;
using Application.Common.Interfaces;
using Application.Common.Settings;
using LanguageExt;
using Microsoft.Extensions.Logging;
using Notify.Interfaces;

namespace Infrastructure.Notify;

// The BLUM-7571 deadline reminder to a data provider that has not yet submitted. Like the closure notification
// (BLUM-7719) there is no SendGrid twin — this exists only as a Notify template — so ConfigureEmailService
// registers it unconditionally rather than behind the EmailProvider switch.
public class NotifyCollectionReminderEmailService : NotifyEmailServiceBase, ICollectionReminderEmailService
{
    private static readonly CultureInfo EnGb = CultureInfo.GetCultureInfo("en-GB");

    public NotifyCollectionReminderEmailService(
        Lazy<IAsyncNotificationClient> notificationClient,
        ApplicationSettings settings,
        ILogger<NotifyCollectionReminderEmailService> logger)
        : base(notificationClient, settings, logger)
    {
        // No constructor guard, for the same reason as NotifyCollectionClosedEmailService: this is resolved both
        // inside SendManualReminderCommandHandler (where a throw would 500 a request the admin has every right to
        // make) and inside the background sweep's own DI scope (where a throw would be swallowed and the sweep
        // would silently never run). Missing config is a logged no-send in SendReminderAsync instead — which is
        // also the state this ships in, since the Notify template does not exist yet.
    }

    public Task<Either<Exception, Unit>> SendReminderAsync(
        string email,
        CollectionReminderNotification notification,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(Settings.ApiKey)
            || string.IsNullOrEmpty(Settings.CollectionReminderTemplateId)
            || string.IsNullOrEmpty(Settings.ClientUrl))
        {
            // EmailNotConfiguredException, not a bare Exception: CollectionReminderNotifier releases its claim on
            // an unconfigured template and keeps it on a failed send, and this is the only thing that tells it
            // which it has. The type is the contract, not the message text.
            return Task.FromResult<Either<Exception, Unit>>(
                new EmailNotConfiguredException("Notify is not configured (ApiKey/CollectionReminderTemplateId/ClientUrl missing); collection reminder not sent."));
        }

        // ClientUrl is already the sign-in page (see appsettings.Development.json), so it is passed through
        // unchanged exactly as NotifyCollectionClosedEmailService does — no path is appended. Where to go once
        // signed in belongs in the template's copy.
        //
        // Key names match the live Notify template's own personalisation fields, confirmed against its "Missing
        // personalisation" 400s rather than guessed — the template was authored independently of the machine-shaped
        // names ("collection_name" etc.) this send used before the template existed. "date" (subject line) and
        // "Deadline" (body) both read as the submission deadline in the template copy, so both carry the same value.
        var deadlineText = notification.SubmissionDeadline.ToString("d MMMM yyyy", EnGb);
        var personalisation = new Dictionary<string, dynamic>
        {
            { "date", deadlineText },
            { "Name", notification.RecipientName },
            { "quarter X", notification.PeriodLabel },
            { "Start date", notification.ReportingPeriodStart.ToString("d MMMM yyyy", EnGb) },
            { "End date", notification.ReportingPeriodEnd.ToString("d MMMM yyyy", EnGb) },
            { "Deadline", deadlineText },
            { "Quarterly report name", notification.CollectionName },
            { "login_url", Settings.ClientUrl },
        };

        return SendAsync(email, Settings.CollectionReminderTemplateId, personalisation, "collection reminder", cancellationToken);
    }
}