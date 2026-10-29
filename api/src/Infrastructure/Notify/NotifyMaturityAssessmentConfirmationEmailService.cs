using System.Globalization;
using Application.Common.Interfaces;
using Application.Common.Settings;
using LanguageExt;
using Microsoft.Extensions.Logging;
using Notify.Interfaces;

namespace Infrastructure.Notify;

// The BLUM-7858 "your maturity assessment has been submitted" email to the submitting LA user. Notify-only, like
// the collection notifications — there is no SendGrid twin, so ConfigureEmailService registers it unconditionally.
public class NotifyMaturityAssessmentConfirmationEmailService : NotifyEmailServiceBase, IMaturityAssessmentConfirmationEmailService
{
    // UK local time, en-GB — matching NotifySubmissionConfirmationEmailService and the exports.
    private static readonly CultureInfo EnGb = CultureInfo.GetCultureInfo("en-GB");
    private static readonly TimeZoneInfo UkTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

    public NotifyMaturityAssessmentConfirmationEmailService(
        Lazy<IAsyncNotificationClient> notificationClient,
        ApplicationSettings settings,
        ILogger<NotifyMaturityAssessmentConfirmationEmailService> logger)
        : base(notificationClient, settings, logger)
    {
        // No constructor guard: this is resolved on the submit path, and a throw would 500 a submit that is already
        // committed. Missing config is a logged no-send in SendConfirmationAsync — the state this ships in, since
        // the Notify template does not exist yet.
    }

    public Task<Either<Exception, Unit>> SendConfirmationAsync(
        string email,
        MaturityAssessmentConfirmation confirmation,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(Settings.ApiKey)
            || string.IsNullOrEmpty(Settings.MaturityAssessmentConfirmationTemplateId)
            || string.IsNullOrEmpty(Settings.ClientUrl))
        {
            return Task.FromResult<Either<Exception, Unit>>(
                new EmailNotConfiguredException("Notify is not configured (ApiKey/MaturityAssessmentConfirmationTemplateId/ClientUrl missing); maturity assessment confirmation not sent."));
        }

        // Links to the frontend confirmation page (client/src/helpers/routes.ts: /reports/maturity/:collectionId/submitted).
        var confirmationUrl = $"{Settings.ClientUrl.TrimEnd('/')}/reports/maturity/{confirmation.CollectionId}/submitted";

        var ukSubmittedAt = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(confirmation.SubmittedAt, DateTimeKind.Utc), UkTimeZone);
        var submittedAt = $"{ukSubmittedAt.ToString("d MMMM yyyy 'at' HH:mm", EnGb)} (UK time)";

        var personalisation = new Dictionary<string, dynamic>
        {
            { "reference_number", confirmation.ReferenceNumber },
            { "submitted_at", submittedAt },
            { "period", confirmation.PeriodLabel },
            { "collection_name", confirmation.CollectionName },
            { "local_authority_name", confirmation.LocalAuthorityName },
            { "confirmation_url", confirmationUrl },
        };

        return SendAsync(email, Settings.MaturityAssessmentConfirmationTemplateId, personalisation, "maturity assessment confirmation", cancellationToken);
    }
}