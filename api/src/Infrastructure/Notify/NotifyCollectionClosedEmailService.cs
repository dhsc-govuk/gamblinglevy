using System.Globalization;
using Application.Common.Interfaces;
using Application.Common.Settings;
using LanguageExt;
using Microsoft.Extensions.Logging;
using Notify.Interfaces;

namespace Infrastructure.Notify;

// The BLUM-7719 "reporting window closed" email to DHSC Super Admins. Unlike the other three purposes there is no
// SendGrid twin: this notification exists only as a Notify template, and a local HTML template nothing would ever
// render is not worth carrying. See ConfigureEmailService for how that changes the registration.
public class NotifyCollectionClosedEmailService : NotifyEmailServiceBase, ICollectionClosedEmailService
{
    // en-GB throughout, matching NotifySubmissionConfirmationEmailService and the report exports.
    private static readonly CultureInfo EnGb = CultureInfo.GetCultureInfo("en-GB");

    public NotifyCollectionClosedEmailService(
        Lazy<IAsyncNotificationClient> notificationClient,
        ApplicationSettings settings,
        ILogger<NotifyCollectionClosedEmailService> logger)
        : base(notificationClient, settings, logger)
    {
        // No constructor guard, for the same reason as NotifySubmissionConfirmationEmailService and more so: this
        // is resolved both inside CloseCollectionCommandHandler (where a throw would 500 a close that has already
        // been committed and audited) and inside the background sweep's own DI scope (where a throw would be
        // swallowed by the hosted service and the sweep would silently never run). Missing config is handled as a
        // logged no-send in SendClosureAsync instead — which is also the state this ships in, since the Notify
        // template does not exist yet.
    }

    public Task<Either<Exception, Unit>> SendClosureAsync(
        string email,
        CollectionClosureNotification notification,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(Settings.ApiKey)
            || string.IsNullOrEmpty(Settings.CollectionClosedTemplateId)
            || string.IsNullOrEmpty(Settings.ClientUrl))
        {
            // EmailNotConfiguredException, not a bare Exception: CollectionClosureNotifier retries an unconfigured
            // template and does not retry a failed send, and this is the only thing that tells it which it has.
            return Task.FromResult<Either<Exception, Unit>>(
                new EmailNotConfiguredException("Notify is not configured (ApiKey/CollectionClosedTemplateId/ClientUrl missing); collection closure notification not sent."));
        }

        // AC3 asks for a link to sign in and download the consolidated report. ClientUrl is already the sign-in
        // page (see appsettings.Development.json), so it is passed through unchanged exactly as
        // NotifyInvitationEmailService does for login_url — no path is appended. Once signed in, the report lives
        // at /admin/reports/{collectionId}.
        //
        // Key names match the BLUM-8095 template copy, not the machine-shaped names ("collection_name" etc.) this
        // send used before that copy existed — same transition NotifyCollectionReminderEmailService and
        // NotifyCollectionWindowOpenedEmailService already made. Verify these against the live template's "Missing
        // personalisation" 400s once Notify__CollectionClosedTemplateId points at a real template.
        var personalisation = new Dictionary<string, dynamic>
        {
            { "name", notification.RecipientName },
            { "Data Collection", notification.CollectionName },
            { "Start date", notification.ReportingPeriodStart.ToString("d MMMM yyyy", EnGb) },
            { "End date", notification.ReportingPeriodEnd.ToString("d MMMM yyyy", EnGb) },
            { "login_url", Settings.ClientUrl },
        };

        return SendAsync(email, Settings.CollectionClosedTemplateId, personalisation, "collection closed", cancellationToken);
    }
}