using System.Globalization;
using System.Net;
using Application.Common.Interfaces;
using Application.Common.Settings;
using LanguageExt;
using Microsoft.Extensions.Logging;
using SendGrid;

namespace Infrastructure.Email;

// Sends the quarterly-report submission confirmation via SendGrid (BLUM-6954). Shares the SendGrid plumbing with
// InvitationEmailService via SendGridEmailServiceBase; contributes only the confirmation subject, template and body.
public class SubmissionConfirmationEmailService : SendGridEmailServiceBase, ISubmissionConfirmationEmailService
{
    private const string Subject = "Your quarterly report has been submitted";

    // Match the report export (QuarterlyReportExportService) so the email and the downloaded report render the same
    // submission timestamp — UK local time, en-GB, never an hour apart during BST.
    private static readonly CultureInfo EnGb = CultureInfo.GetCultureInfo("en-GB");
    private static readonly TimeZoneInfo UkTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

    private static readonly Lazy<string> HtmlTemplate = new(() => LoadTemplate("SubmissionConfirmationEmail.html"));

    public SubmissionConfirmationEmailService(
        ISendGridClient sendGridClient,
        ApplicationSettings settings,
        ILogger<SubmissionConfirmationEmailService> logger)
        : base(sendGridClient, settings, logger)
    {
        // Unlike InvitationEmailService, config presence is NOT guarded here: this service is resolved on the submit
        // path, and a construction-time throw would 500 the (best-effort) submit. Missing SenderEmail/ClientUrl is
        // instead handled as a logged no-send in SendConfirmationAsync.
    }

    public Task<Either<Exception, Unit>> SendConfirmationAsync(
        string email,
        SubmissionConfirmation confirmation,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(Settings.SenderEmail) || string.IsNullOrEmpty(Settings.ClientUrl))
        {
            return Task.FromResult<Either<Exception, Unit>>(
                new Exception("SendGrid is not configured (SenderEmail/ClientUrl missing); submission confirmation not sent."));
        }

        (string PlainText, string Html) BuildBody()
        {
            // Links to the frontend confirmation page (client/src/helpers/routes.ts: /reports/quarterly/:collectionId/submitted).
            var confirmationUrl = $"{Settings.ClientUrl.TrimEnd('/')}/reports/quarterly/{confirmation.CollectionId}/submitted";

            // Stored SubmittedAt is UTC; render in UK local time to match the report export (see EnGb/UkTimeZone above).
            var ukSubmittedAt = TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(confirmation.SubmittedAt, DateTimeKind.Utc), UkTimeZone);
            var submittedAt = $"{ukSubmittedAt.ToString("d MMMM yyyy 'at' HH:mm", EnGb)} (UK time)";

            // Admin-entered names are HTML-encoded before injection into the HTML body so markup/entity-like characters
            // in a collection or organisation name render literally. Reference/quarter/URL are system-composed and safe.
            var html = HtmlTemplate.Value
                .Replace("{{ReferenceNumber}}", confirmation.ReferenceNumber)
                .Replace("{{SubmittedAt}}", submittedAt)
                .Replace("{{Quarter}}", confirmation.QuarterLabel)
                .Replace("{{CollectionName}}", WebUtility.HtmlEncode(confirmation.CollectionName))
                .Replace("{{LocalAuthorityName}}", WebUtility.HtmlEncode(confirmation.LocalAuthorityName))
                .Replace("{{ConfirmationUrl}}", confirmationUrl);

            var plainText =
                "Your quarterly report has been submitted.\n\n" +
                $"Reference number: {confirmation.ReferenceNumber}\n" +
                $"Submitted: {submittedAt}\n" +
                $"Quarter: {confirmation.QuarterLabel}\n" +
                $"Collection: {confirmation.CollectionName}\n" +
                $"Local authority: {confirmation.LocalAuthorityName}\n\n" +
                "View your confirmation:\n" +
                $"{confirmationUrl}\n\n" +
                "Department of Health & Social Care";

            return (plainText, html);
        }

        return SendAsync(email, Subject, BuildBody, "submission confirmation", cancellationToken);
    }
}