using System.Reflection;
using Application.Common.Settings;
using Domain.Common;
using LanguageExt;
using Microsoft.Extensions.Logging;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace Infrastructure.Email;

// Shared SendGrid plumbing for the transactional email services (InvitationEmailService, SubmissionConfirmationEmailService).
// Owns the message build, the bounded send + status check + body read, and the try/catch that surfaces failures as a Left
// so a single fix (error handling, logging, retry) lives in one place. Subclasses contribute only their subject, embedded
// template and body composition; per-service config validation stays in the subclass (it deliberately differs).
public abstract class SendGridEmailServiceBase
{
    private const string ServiceName = "Share progress preventing gambling harms";

    // A hung/slow SendGrid must not stall the caller (every caller treats the result as best-effort) — cap the send well
    // below any upstream gateway timeout so a failure stays a logged Left rather than hanging the request.
    // Overridable because the ceiling a caller can afford depends entirely on what it is holding up: a submit the user
    // has already committed to can wait, a sign-in in progress cannot.
    protected virtual TimeSpan SendTimeout => TimeSpan.FromSeconds(15);

    private readonly ISendGridClient _sendGridClient;
    private readonly ILogger _logger;

    protected SendGridSettings Settings { get; }

    protected SendGridEmailServiceBase(
        ISendGridClient sendGridClient,
        ApplicationSettings settings,
        ILogger logger)
    {
        Guard.NotNull(sendGridClient, nameof(sendGridClient));
        Guard.NotNull(settings, nameof(settings));
        Guard.NotNull(logger, nameof(logger));

        _sendGridClient = sendGridClient;
        Settings = settings.SendGrid;
        _logger = logger;
    }

    // Builds the body (inside the try, so template access/substitution can never throw past the best-effort contract),
    // sends via SendGrid under a bounded timeout, and folds the outcome into an Either. Callers log the Left.
    protected async Task<Either<Exception, Unit>> SendAsync(
        string toEmail,
        string subject,
        Func<(string PlainText, string Html)> buildBody,
        string logLabel,
        CancellationToken cancellationToken)
    {
        try
        {
            var (plainText, html) = buildBody();

            var message = MailHelper.CreateSingleEmail(
                new EmailAddress(Settings.SenderEmail, ServiceName),
                new EmailAddress(toEmail),
                subject,
                plainText,
                html);

            using var sendCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            sendCts.CancelAfter(SendTimeout);

            var response = await _sendGridClient.SendEmailAsync(message, sendCts.Token);

            if ((int)response.StatusCode is < 200 or >= 300)
            {
                var body = await response.Body.ReadAsStringAsync(sendCts.Token);
                return new Exception($"SendGrid returned {(int)response.StatusCode} sending the {logLabel} to {toEmail}: {body}");
            }

            _logger.LogInformation("[{Label}] sent to {Email}", logLabel, toEmail);
            return Unit.Default;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    protected static string LoadTemplate(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly
            .GetManifestResourceNames()
            .Single(name => name.EndsWith(fileName, StringComparison.Ordinal));

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}