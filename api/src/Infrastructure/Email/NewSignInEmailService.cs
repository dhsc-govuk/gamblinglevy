using System.Globalization;
using System.Net;
using Application.Common.Interfaces;
using Application.Common.Settings;
using LanguageExt;
using Microsoft.Extensions.Logging;
using SendGrid;

namespace Infrastructure.Email;

// Sends the "new sign-in to your account" notification via SendGrid (BLUM-7241). Shares the SendGrid plumbing with
// InvitationEmailService and SubmissionConfirmationEmailService via SendGridEmailServiceBase; contributes only the
// subject, template and body.
public class NewSignInEmailService : SendGridEmailServiceBase, INewSignInEmailService
{
    private const string Subject = "New sign-in to your account";

    // Match SubmissionConfirmationEmailService: the user is being asked "was this you, at this time?", and a UTC
    // timestamp an hour out during BST is exactly the sort of discrepancy that makes a genuine sign-in look hostile.
    private static readonly CultureInfo EnGb = CultureInfo.GetCultureInfo("en-GB");
    private static readonly TimeZoneInfo UkTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

    private static readonly Lazy<string> HtmlTemplate = new(() => LoadTemplate("NewSignInEmail.html"));

    /// <summary>
    /// Tighter than the 15s default because this send sits inside POST /mfa/verify: until it returns, the
    /// session cookie is not set and the user is watching a spinner on the code-entry screen. There is no
    /// background worker in this API to hand the send off to, so the honest control is a ceiling on how much
    /// of a third party's bad day the sign-in can inherit. Losing the notification is the lesser harm — the
    /// sessions list is the authoritative record either way.
    /// </summary>
    protected override TimeSpan SendTimeout => TimeSpan.FromSeconds(5);

    public NewSignInEmailService(
        ISendGridClient sendGridClient,
        ApplicationSettings settings,
        ILogger<NewSignInEmailService> logger)
        : base(sendGridClient, settings, logger)
    {
        // Config presence is deliberately NOT guarded here, for the same reason as SubmissionConfirmationEmailService
        // and unlike InvitationEmailService: this service is resolved on the MFA verify path, so a construction-time
        // throw would 500 a sign-in that has already succeeded. Missing config is a logged no-send instead.
    }

    public Task<Either<Exception, Unit>> SendNewSignInAsync(
        string email,
        NewSignInNotification notification,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(Settings.SenderEmail) || string.IsNullOrEmpty(Settings.ClientUrl))
        {
            return Task.FromResult<Either<Exception, Unit>>(
                new Exception("SendGrid is not configured (SenderEmail/ClientUrl missing); new sign-in notification not sent."));
        }

        (string PlainText, string Html) BuildBody()
        {
            // The sessions list the email asks the user to act on (client/src/helpers/routes.ts: /account/sessions).
            var sessionsUrl = $"{Settings.ClientUrl.TrimEnd('/')}/account/sessions";

            var ukSignedInAt = TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(notification.SignedInAt, DateTimeKind.Utc), UkTimeZone);
            var signedInAt = $"{ukSignedInAt.ToString("d MMMM yyyy 'at' HH:mm", EnGb)} (UK time)";

            // The device label and IP are derived from request headers an attacker sets, and the user's name is
            // admin-entered, so all three are HTML-encoded before injection. The URL and timestamp are system-composed.
            var html = HtmlTemplate.Value
                .Replace("{{UserName}}", WebUtility.HtmlEncode(notification.UserName))
                .Replace("{{DeviceLabel}}", WebUtility.HtmlEncode(notification.DeviceLabel))
                .Replace("{{IpAddress}}", WebUtility.HtmlEncode(notification.IpAddress))
                .Replace("{{SignedInAt}}", signedInAt)
                .Replace("{{SessionsUrl}}", sessionsUrl);

            var plainText =
                $"Hello {notification.UserName},\n\n" +
                "Somebody signed in to your Share progress preventing gambling harms account on a device that was not already signed in.\n\n" +
                $"Device: {notification.DeviceLabel}\n" +
                $"IP address: {notification.IpAddress}\n" +
                $"Signed in: {signedInAt}\n\n" +
                "If this was you, you do not need to do anything.\n\n" +
                "If it was not you, sign in and end that session straight away, then change your password:\n" +
                $"{sessionsUrl}\n\n" +
                "Department of Health & Social Care";

            return (plainText, html);
        }

        return SendAsync(email, Subject, BuildBody, "new sign-in notification", cancellationToken);
    }
}