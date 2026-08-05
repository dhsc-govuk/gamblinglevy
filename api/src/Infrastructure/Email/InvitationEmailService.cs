using Application.Common.Interfaces;
using Application.Common.Settings;
using Domain.Common;
using Domain.ValueObjects;
using LanguageExt;
using Microsoft.Extensions.Logging;
using SendGrid;

namespace Infrastructure.Email;

public class InvitationEmailService : SendGridEmailServiceBase, IInvitationEmailService
{
    private const string Subject = "You've been added to Share progress preventing gambling harms";

    private static readonly Lazy<string> HtmlTemplate = new(() => LoadTemplate("InvitationEmail.html"));

    public InvitationEmailService(
        ISendGridClient sendGridClient,
        ApplicationSettings settings,
        ILogger<InvitationEmailService> logger)
        : base(sendGridClient, settings, logger)
    {
        // Invitation is part of user creation; a misconfigured environment should fail loudly at construction rather
        // than silently drop invites. (SubmissionConfirmationEmailService deliberately validates at send-time instead.)
        Guard.NotNullOrEmpty(settings.SendGrid.SenderEmail, nameof(settings.SendGrid.SenderEmail));
        Guard.NotNullOrEmpty(settings.SendGrid.ClientUrl, nameof(settings.SendGrid.ClientUrl));
    }

    public Task<Either<Exception, Unit>> SendInvitationAsync(string email, Name name, CancellationToken cancellationToken)
    {
        (string PlainText, string Html) BuildBody()
        {
            var signInUrl = Settings.ClientUrl;
            var userName = name.ToString();

            var html = HtmlTemplate.Value
                .Replace("{{UserName}}", userName)
                .Replace("{{SignInUrl}}", signInUrl);

            var plainText =
                $"Hello {userName},\n\n" +
                "An account has been created for you to use this service. " +
                "Sign in with this email address to get started:\n\n" +
                $"{signInUrl}\n\n" +
                "Department of Health & Social Care";

            return (plainText, html);
        }

        return SendAsync(email, Subject, BuildBody, "invitation", cancellationToken);
    }
}