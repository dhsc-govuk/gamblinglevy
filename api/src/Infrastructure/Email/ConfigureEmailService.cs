using Application.Common.Interfaces;
using Application.Common.Settings;
using Microsoft.Extensions.DependencyInjection;
using SendGrid;

namespace Infrastructure.Email;

public static class ConfigureEmailService
{
    extension(IServiceCollection services)
    {
        public void AddEmailServices()
        {
            services.SetupSendGridClient();
            services.AddScoped<IInvitationEmailService, InvitationEmailService>();
            services.AddScoped<ISubmissionConfirmationEmailService, SubmissionConfirmationEmailService>();
            services.AddScoped<INewSignInEmailService, NewSignInEmailService>();

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
    }
}