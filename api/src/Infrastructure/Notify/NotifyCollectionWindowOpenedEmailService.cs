using System.Globalization;
using Application.Common.Interfaces;
using Application.Common.Settings;
using LanguageExt;
using Microsoft.Extensions.Logging;
using Notify.Interfaces;

namespace Infrastructure.Notify;

// The BLUM-7721 "your reporting window is open" email to a data provider contact. Like the closure notification
// and the deadline reminder there is no SendGrid twin — this exists only as a Notify template — so
// ConfigureEmailService registers it unconditionally rather than behind the EmailProvider switch.
public class NotifyCollectionWindowOpenedEmailService : NotifyEmailServiceBase, ICollectionWindowOpenedEmailService
{
    private static readonly CultureInfo EnGb = CultureInfo.GetCultureInfo("en-GB");

    public NotifyCollectionWindowOpenedEmailService(
        Lazy<IAsyncNotificationClient> notificationClient,
        ApplicationSettings settings,
        ILogger<NotifyCollectionWindowOpenedEmailService> logger)
        : base(notificationClient, settings, logger)
    {
        // No constructor guard, for the same reason as NotifyCollectionReminderEmailService: this is resolved
        // inside the background sweep's own DI scope, where a throw would be swallowed by the hosted service and
        // the sweep would silently never run. Missing config is a logged no-send in SendWindowOpenedAsync
        // instead — which is also the state this ships in, since the Notify template does not exist yet.
    }

    public Task<Either<Exception, Unit>> SendWindowOpenedAsync(
        string email,
        CollectionWindowOpenedNotification notification,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(Settings.ApiKey)
            || string.IsNullOrEmpty(Settings.CollectionWindowOpenedTemplateId)
            || string.IsNullOrEmpty(Settings.ClientUrl))
        {
            // EmailNotConfiguredException, not a bare Exception: CollectionReminderNotifier releases its claim on
            // an unconfigured template and keeps it on a failed send, and this is the only thing that tells it
            // which it has. The type is the contract, not the message text.
            return Task.FromResult<Either<Exception, Unit>>(
                new EmailNotConfiguredException("Notify is not configured (ApiKey/CollectionWindowOpenedTemplateId/ClientUrl missing); collection window-opened notification not sent."));
        }

        // ClientUrl is already the sign-in page (see appsettings.Development.json), so it is passed through
        // unchanged exactly as the other collection notifications do — no path is appended. Where to go once
        // signed in belongs in the template's copy.
        //
        // Key names match the BLUM-8094 template copy (its wording is near-identical to the deadline reminder's),
        // not the machine-shaped names ("collection_name" etc.) this send used before that copy existed — same
        // transition NotifyCollectionReminderEmailService already made. Verify these against the live template's
        // "Missing personalisation" 400s once Notify__CollectionWindowOpenedTemplateId points at a real template,
        // the same way that service's keys were confirmed rather than guessed.
        var personalisation = new Dictionary<string, dynamic>
        {
            { "Name", notification.RecipientName },
            { "Quarter X", notification.PeriodLabel },
            { "Start date", notification.ReportingPeriodStart.ToString("d MMMM yyyy", EnGb) },
            { "End date", notification.ReportingPeriodEnd.ToString("d MMMM yyyy", EnGb) },
            { "Deadline", notification.SubmissionDeadline.ToString("d MMMM yyyy", EnGb) },
            { "Quarterly report name", notification.CollectionName },
            { "login_url", Settings.ClientUrl },
        };

        return SendAsync(email, Settings.CollectionWindowOpenedTemplateId, personalisation, "collection window opened", cancellationToken);
    }
}