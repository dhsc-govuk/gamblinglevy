using Application.Common.Interfaces;
using Application.Common.Settings;
using Infrastructure.Notify;
using Microsoft.Extensions.DependencyInjection;
using Notify.Client;
using Notify.Interfaces;
using SendGrid;

namespace Infrastructure.Email;

public static class ConfigureEmailService
{
    extension(IServiceCollection services)
    {
        public void AddEmailServices()
        {
            services.SetupSendGridClient();
            services.SetupNotifyClient();

            // ApplicationSettings.EmailProvider picks which concrete type backs each interface; it defaults to
            // SendGrid everywhere, so adding the Notify alternative changes nothing until an environment
            // explicitly opts in. ActivatorUtilities builds whichever concrete type is chosen with its normal
            // constructor DI, so neither implementation needs to know about the other.
            services.AddScoped<IInvitationEmailService>(provider =>
                provider.GetRequiredService<ApplicationSettings>().EmailProvider == EmailProvider.Notify
                    ? ActivatorUtilities.CreateInstance<NotifyInvitationEmailService>(provider)
                    : ActivatorUtilities.CreateInstance<InvitationEmailService>(provider));

            services.AddScoped<ISubmissionConfirmationEmailService>(provider =>
                provider.GetRequiredService<ApplicationSettings>().EmailProvider == EmailProvider.Notify
                    ? ActivatorUtilities.CreateInstance<NotifySubmissionConfirmationEmailService>(provider)
                    : ActivatorUtilities.CreateInstance<SubmissionConfirmationEmailService>(provider));

            services.AddScoped<INewSignInEmailService>(provider =>
                provider.GetRequiredService<ApplicationSettings>().EmailProvider == EmailProvider.Notify
                    ? ActivatorUtilities.CreateInstance<NotifyNewSignInEmailService>(provider)
                    : ActivatorUtilities.CreateInstance<NewSignInEmailService>(provider));

            // Registered unconditionally rather than behind the EmailProvider switch above: the closure
            // notification (BLUM-7719) exists only as a Notify template, so there is no SendGrid twin to pick
            // between. Wherever Notify is unconfigured — including the test host, which runs EmailProvider
            // SendGrid — NotifyCollectionClosedEmailService's send-time guard makes it a logged no-send.
            services.AddScoped<ICollectionClosedEmailService, NotifyCollectionClosedEmailService>();

            // Same reasoning for the two BLUM-7571 reminder emails: Notify-only, no SendGrid twin, and the test
            // host runs EmailProvider SendGrid yet still has to resolve them.
            services.AddScoped<ICollectionReminderEmailService, NotifyCollectionReminderEmailService>();
            services.AddScoped<ICollectionReminderSummaryEmailService, NotifyCollectionReminderSummaryEmailService>();

            // And the BLUM-8155 "reporting window closed" email to LA users whose organisation did not submit.
            services.AddScoped<ICollectionWindowClosedLaEmailService, NotifyCollectionWindowClosedLaEmailService>();

            // And for the two BLUM-7721 window-opened emails.
            services.AddScoped<ICollectionWindowOpenedEmailService, NotifyCollectionWindowOpenedEmailService>();
            services.AddScoped<ICollectionWindowOpenedSummaryEmailService, NotifyCollectionWindowOpenedSummaryEmailService>();

            // And the BLUM-7858 maturity-assessment confirmation.
            services.AddScoped<IMaturityAssessmentConfirmationEmailService, NotifyMaturityAssessmentConfirmationEmailService>();

            // And the BLUM-XXXX temporary-password notification (Enable/Reset password login).
            services.AddScoped<ITemporaryPasswordEmailService, NotifyTemporaryPasswordEmailService>();

            // Also registered lazily, and MfaSessionService takes it that way on purpose (BLUM-7241).
            // MfaRequiredMiddleware method-injects IMfaSessionService, and ASP.NET Core resolves every
            // InvokeAsync parameter before the method body runs — before the BypassForTesting and exempt-path
            // early returns. Taking the transport directly would therefore build a SendGridClient, and with it
            // a fresh HttpClient, on EVERY authenticated request rather than on the rare sign-in that actually
            // sends one; under load that exhausts outbound sockets. Deferring construction also confines a
            // failure in the service's own static setup to the send, instead of 500ing every request through
            // the gate — including /health.
            services.AddScoped(provider => new Lazy<INewSignInEmailService>(provider.GetRequiredService<INewSignInEmailService>));
            services.AddScoped<IEmailService, LoggingEmailService>();
        }

        private void SetupSendGridClient()
        {
            services.AddScoped<ISendGridClient>(provider =>
            {
                var settings = provider.GetRequiredService<ApplicationSettings>();

                return new SendGridClient(settings.SendGrid.EmailApiKey);
            });
        }

        private void SetupNotifyClient()
        {
            // Registered as a Lazy<T>, not IAsyncNotificationClient directly: NotificationClient's constructor
            // validates the API key's shape immediately, so an eager registration would throw here — during
            // ASP.NET Core's constructor-parameter resolution for whatever command handler depends on
            // IInvitationEmailService/ISubmissionConfirmationEmailService/INewSignInEmailService — before
            // NotifyEmailServiceBase.SendAsync's own try/catch ever runs. That turns the deliberate
            // "missing config = logged no-send" contract on the submit and sign-in paths into an unconditional
            // 500. Deferring construction to first use (NotifyEmailServiceBase.SendAsync accessing .Value) means
            // a missing or malformed key surfaces there instead, as a caught exception like any other send failure.
            // settings.Notify?. also protects a config binding that omitted the Notify section entirely.
            services.AddScoped(provider => new Lazy<IAsyncNotificationClient>(() =>
            {
                var settings = provider.GetRequiredService<ApplicationSettings>();

                return new NotificationClient(settings.Notify?.ApiKey);
            }));
        }
    }
}