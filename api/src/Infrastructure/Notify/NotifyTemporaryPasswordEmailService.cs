using Application.Common.Interfaces;
using Application.Common.Settings;
using LanguageExt;
using Microsoft.Extensions.Logging;
using Notify.Interfaces;

namespace Infrastructure.Notify;

// The BLUM-XXXX "your temporary password" email. Like NotifyCollectionClosedEmailService there is no SendGrid
// twin — this notification exists only as a Notify template. See ConfigureEmailService for how that changes
// the registration.
public class NotifyTemporaryPasswordEmailService : NotifyEmailServiceBase, ITemporaryPasswordEmailService
{
    public NotifyTemporaryPasswordEmailService(
        Lazy<IAsyncNotificationClient> notificationClient,
        ApplicationSettings settings,
        ILogger<NotifyTemporaryPasswordEmailService> logger)
        : base(notificationClient, settings, logger)
    {
        // No constructor guard, same reasoning as NotifyCollectionClosedEmailService: the account's password
        // has already been changed in Entra by the time this is called, so a throw here would 500 a request
        // that has already succeeded. Missing config is handled as a logged no-send in
        // SendTemporaryPasswordAsync instead — the state this ships in, since the Notify template does not
        // exist yet.
    }

    public bool IsConfigured =>
        !string.IsNullOrEmpty(Settings.ApiKey)
        && !string.IsNullOrEmpty(Settings.TemporaryPasswordTemplateId)
        && !string.IsNullOrEmpty(Settings.ClientUrl);

    public Task<Either<Exception, Unit>> SendTemporaryPasswordAsync(
        string email,
        TemporaryPasswordNotification notification,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return Task.FromResult<Either<Exception, Unit>>(
                new EmailNotConfiguredException("Notify is not configured (ApiKey/TemporaryPasswordTemplateId/ClientUrl missing); temporary password not sent."));
        }

        // Key names are placeholders pending the real Notify template — re-check against the dashboard once
        // Notify__TemporaryPasswordTemplateId points at a real template, same as every other email type here.
        var personalisation = new Dictionary<string, dynamic>
        {
            { "name", notification.RecipientName },
            { "temporary_password", notification.TemporaryPassword },
            { "login_url", Settings.ClientUrl },
        };

        return SendAsync(email, Settings.TemporaryPasswordTemplateId, personalisation, "temporary password", cancellationToken);
    }
}