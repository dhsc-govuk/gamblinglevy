using System.Globalization;
using Application.Common.Interfaces;
using Application.Common.Settings;
using LanguageExt;
using Microsoft.Extensions.Logging;
using Notify.Interfaces;

namespace Infrastructure.Notify;

// GOV.UK Notify alternative to NewSignInEmailService (Infrastructure/Email/), selected only when
// ApplicationSettings.EmailProvider is Notify. See NotifyInvitationEmailService for why there is no local HTML
// template and why personalisation keys are snake_case.
public class NotifyNewSignInEmailService : NotifyEmailServiceBase, INewSignInEmailService
{
    // Match SubmissionConfirmation: the user is being asked "was this you, at this time?", and a UTC timestamp an
    // hour out during BST is exactly the sort of discrepancy that makes a genuine sign-in look hostile.
    private static readonly CultureInfo EnGb = CultureInfo.GetCultureInfo("en-GB");
    private static readonly TimeZoneInfo UkTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

    // Matches NewSignInEmailService: this send sits inside POST /mfa/verify, so it must stay well under the
    // caller's tolerance rather than the general 15s default.
    protected override TimeSpan SendTimeout => TimeSpan.FromSeconds(5);

    public NotifyNewSignInEmailService(
        Lazy<IAsyncNotificationClient> notificationClient,
        ApplicationSettings settings,
        ILogger<NotifyNewSignInEmailService> logger)
        : base(notificationClient, settings, logger)
    {
        // Config presence is deliberately NOT guarded here, for the same reason as
        // NotifySubmissionConfirmationEmailService and unlike NotifyInvitationEmailService: this service is
        // resolved on the MFA verify path, so a construction-time throw would 500 a sign-in that has already
        // succeeded. Missing config is a logged no-send instead.
    }

    public Task<Either<Exception, Unit>> SendNewSignInAsync(
        string email,
        NewSignInNotification notification,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(Settings.ApiKey)
            || string.IsNullOrEmpty(Settings.NewSignInTemplateId)
            || string.IsNullOrEmpty(Settings.ClientUrl))
        {
            return Task.FromResult<Either<Exception, Unit>>(
                new Exception("Notify is not configured (ApiKey/NewSignInTemplateId/ClientUrl missing); new sign-in notification not sent."));
        }

        // The sessions list the email asks the user to act on (client/src/helpers/routes.ts: /account/sessions).
        var sessionsUrl = $"{Settings.ClientUrl.TrimEnd('/')}/account/sessions";

        var ukSignedInAt = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(notification.SignedInAt, DateTimeKind.Utc), UkTimeZone);
        var signedInAt = $"{ukSignedInAt.ToString("d MMMM yyyy 'at' HH:mm", EnGb)} (UK time)";

        // Notify renders personalisation as plain text inside its own dashboard-managed template, so device label
        // and IP address (both derived from attacker-controlled request headers) do not need HTML-encoding the
        // way NewSignInEmailService's raw HTML build does.
        var personalisation = new Dictionary<string, dynamic>
        {
            { "user_name", notification.UserName },
            { "device_label", notification.DeviceLabel },
            { "ip_address", notification.IpAddress },
            { "signed_in_at", signedInAt },
            { "sessions_url", sessionsUrl },
        };

        return SendAsync(email, Settings.NewSignInTemplateId, personalisation, "new sign-in notification", cancellationToken);
    }
}