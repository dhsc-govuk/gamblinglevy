using Application.Common.Settings;
using Domain.Common;
using LanguageExt;
using Microsoft.Extensions.Logging;
using Notify.Interfaces;

namespace Infrastructure.Notify;

// Shared GOV.UK Notify plumbing for the transactional email services (NotifyInvitationEmailService,
// NotifySubmissionConfirmationEmailService, NotifyNewSignInEmailService). Mirrors SendGridEmailServiceBase's
// contract (bounded send, try/catch folded into an Either, success-only logging) so a purpose service behaves the
// same regardless of which provider ApplicationSettings.EmailProvider selects.
//
// One real difference from SendGridEmailServiceBase: IAsyncNotificationClient.SendEmailAsync takes no
// CancellationToken, so WaitAsync only bounds how long the caller waits — it cannot abort the underlying HTTP
// call once the SDK has started it. Accepted because, exactly like the SendGrid path, every caller already treats
// the result as best-effort and logs a Left rather than propagating it.
//
// The client is injected as a Lazy<T> rather than IAsyncNotificationClient directly: NotificationClient's
// constructor validates the API key's shape immediately, so an eager registration throws while ASP.NET Core is
// still resolving constructor parameters for whatever command handler depends on one of these services — before
// NotifySubmissionConfirmationEmailService/NotifyNewSignInEmailService's own deliberate "missing config = logged
// no-send" send-time checks ever run, turning a best-effort no-send into a 500 on an already-committed submit or
// an already-succeeded sign-in. Deferring construction to first use means that failure happens inside the
// try/catch below instead, which is exactly the contract every caller expects.
public abstract class NotifyEmailServiceBase
{
    protected virtual TimeSpan SendTimeout => TimeSpan.FromSeconds(15);

    private readonly Lazy<IAsyncNotificationClient> _notificationClient;
    private readonly ILogger _logger;

    protected NotifySettings Settings { get; }

    protected NotifyEmailServiceBase(
        Lazy<IAsyncNotificationClient> notificationClient,
        ApplicationSettings settings,
        ILogger logger)
    {
        Guard.NotNull(notificationClient, nameof(notificationClient));
        Guard.NotNull(settings, nameof(settings));
        Guard.NotNull(logger, nameof(logger));

        _notificationClient = notificationClient;
        Settings = settings.Notify;
        _logger = logger;
    }

    protected async Task<Either<Exception, Unit>> SendAsync(
        string toEmail,
        string templateId,
        Dictionary<string, dynamic> personalisation,
        string logLabel,
        CancellationToken cancellationToken)
    {
        try
        {
            await _notificationClient.Value
                .SendEmailAsync(toEmail, templateId, personalisation)
                .WaitAsync(SendTimeout, cancellationToken);

            _logger.LogInformation("[{Label}] sent to {Email}", logLabel, toEmail);
            return Unit.Default;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }
}