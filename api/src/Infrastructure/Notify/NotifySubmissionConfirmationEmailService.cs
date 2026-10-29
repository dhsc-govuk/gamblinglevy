using Application.Common.Interfaces;
using Application.Common.Settings;
using LanguageExt;
using Microsoft.Extensions.Logging;
using Notify.Interfaces;

namespace Infrastructure.Notify;

// GOV.UK Notify alternative to SubmissionConfirmationEmailService (Infrastructure/Email/), selected only when
// ApplicationSettings.EmailProvider is Notify. See NotifyInvitationEmailService for why there is no local HTML
// template.
public class NotifySubmissionConfirmationEmailService : NotifyEmailServiceBase, ISubmissionConfirmationEmailService
{
    public NotifySubmissionConfirmationEmailService(
        Lazy<IAsyncNotificationClient> notificationClient,
        ApplicationSettings settings,
        ILogger<NotifySubmissionConfirmationEmailService> logger)
        : base(notificationClient, settings, logger)
    {
        // Unlike NotifyInvitationEmailService, config presence is NOT guarded here: this service is resolved on
        // the submit path, and a construction-time throw would 500 the (best-effort) submit. Missing
        // ApiKey/TemplateId/ClientUrl is instead handled as a logged no-send in SendConfirmationAsync, mirroring
        // SubmissionConfirmationEmailService.
    }

    public Task<Either<Exception, Unit>> SendConfirmationAsync(
        string email,
        SubmissionConfirmation confirmation,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(Settings.ApiKey)
            || string.IsNullOrEmpty(Settings.SubmissionConfirmationTemplateId)
            || string.IsNullOrEmpty(Settings.ClientUrl))
        {
            return Task.FromResult<Either<Exception, Unit>>(
                new Exception("Notify is not configured (ApiKey/SubmissionConfirmationTemplateId/ClientUrl missing); submission confirmation not sent."));
        }

        // Notify renders personalisation as plain text inside its own dashboard-managed template, not raw HTML
        // string-substitution, so there is no HTML-injection surface here to encode against.
        //
        // Key names (name, report name, login_url) must match the "LA users: report submitted" Notify template
        // (BLUM-7575) exactly, including the space in "report name" — Notify placeholder names are not required
        // to be snake_case, and this template's is a space rather than an underscore.
        var personalisation = new Dictionary<string, dynamic>
        {
            { "name", confirmation.RecipientName },
            { "report name", confirmation.CollectionName },
            { "login_url", Settings.ClientUrl },
        };

        return SendAsync(email, Settings.SubmissionConfirmationTemplateId, personalisation, "submission confirmation", cancellationToken);
    }
}