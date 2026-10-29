using System.Globalization;
using Application.Common.Interfaces;
using Application.Common.Settings;
using LanguageExt;
using Microsoft.Extensions.Logging;
using Notify.Interfaces;

namespace Infrastructure.Notify;

// The BLUM-8155 "reporting window closed" notification to LA users of an organisation that did not submit. Like
// the closure notification (BLUM-7719) and the deadline reminder (BLUM-7571) there is no SendGrid twin — this
// exists only as a Notify template — so ConfigureEmailService registers it unconditionally rather than behind
// the EmailProvider switch.
public class NotifyCollectionWindowClosedLaEmailService : NotifyEmailServiceBase, ICollectionWindowClosedLaEmailService
{
    private static readonly CultureInfo EnGb = CultureInfo.GetCultureInfo("en-GB");

    public NotifyCollectionWindowClosedLaEmailService(
        Lazy<IAsyncNotificationClient> notificationClient,
        ApplicationSettings settings,
        ILogger<NotifyCollectionWindowClosedLaEmailService> logger)
        : base(notificationClient, settings, logger)
    {
        // No constructor guard, same reasoning as NotifyCollectionClosedEmailService/NotifyCollectionReminderEmailService:
        // this is resolved inside the background sweep's own DI scope, where a throw would be swallowed and the
        // sweep would silently never run. Missing config is a logged no-send in SendWindowClosedAsync instead —
        // which is also the state this ships in, since the Notify template does not exist yet.
    }

    public Task<Either<Exception, Unit>> SendWindowClosedAsync(
        string email,
        CollectionWindowClosedLaNotification notification,
        CancellationToken cancellationToken)
    {
        // Deliberately NOT also guarding on Settings.ClientUrl, unlike NotifyCollectionReminderEmailService: this
        // email's personalisation contract is Name/Deadline/Data collection, with no sign-in link — guarding on
        // an unused setting would misreport "not configured" when the real template id is set but ClientUrl
        // happens to be blank.
        if (string.IsNullOrEmpty(Settings.ApiKey) || string.IsNullOrEmpty(Settings.CollectionWindowClosedLaTemplateId))
        {
            // EmailNotConfiguredException, not a bare Exception: CollectionWindowClosedLaNotifier releases its
            // claim on an unconfigured template and keeps it on a failed send, and this is the only thing that
            // tells it which it has. The type is the contract, not the message text.
            return Task.FromResult<Either<Exception, Unit>>(
                new EmailNotConfiguredException("Notify is not configured (ApiKey/CollectionWindowClosedLaTemplateId missing); window closed notification not sent."));
        }

        // Key names confirmed against the live template's actual "Missing personalisation" 400 rather than
        // guessed from the ticket's field list — the deployed Notify template uses "Data collection", not
        // "Quarterly report name" (BLUM-8155's AC calls the field "Quarterly report name", but the template as
        // built uses the CollectionWindowClosedLaTemplateId's own placeholder name; Name and Deadline matched on
        // the first attempt). Same convention NotifyCollectionReminderEmailService's comment describes.
        var personalisation = new Dictionary<string, dynamic>
        {
            { "Name", notification.RecipientName },
            { "Deadline", notification.SubmissionDeadline.ToString("d MMMM yyyy", EnGb) },
            { "Data collection", notification.CollectionName },
        };

        return SendAsync(email, Settings.CollectionWindowClosedLaTemplateId, personalisation, "window closed (LA)", cancellationToken);
    }
}