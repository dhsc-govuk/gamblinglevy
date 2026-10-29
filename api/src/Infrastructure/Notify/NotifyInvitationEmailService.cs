using Application.Common.Interfaces;
using Application.Common.Settings;
using Domain.Common;
using Domain.ValueObjects;
using LanguageExt;
using Microsoft.Extensions.Logging;
using Notify.Interfaces;

namespace Infrastructure.Notify;

// GOV.UK Notify alternative to InvitationEmailService (Infrastructure/Email/), selected only when
// ApplicationSettings.EmailProvider is Notify. Notify templates the message server-side by TemplateId, so there is
// no local HTML template here — only a personalisation dictionary. Key names (Name, login_url) must match the
// ((Name)) / (login_url) placeholders in the real Notify dashboard templates (BLUM-7568) exactly, including case —
// Notify's 400 on a missing key names the placeholder verbatim, so this is not the snake_case convention used
// elsewhere in this service.
//
// Invitation copy differs by role — "DHSC users: new account", "LA Admin users: new account" and "LA Lead users:
// new account" are three separate Notify templates (BLUM-7568), so the template ID is picked per UserRole rather
// than being a single Settings.Notify value.
public class NotifyInvitationEmailService : NotifyEmailServiceBase, IInvitationEmailService
{
    public NotifyInvitationEmailService(
        Lazy<IAsyncNotificationClient> notificationClient,
        ApplicationSettings settings,
        ILogger<NotifyInvitationEmailService> logger)
        : base(notificationClient, settings, logger)
    {
        // Mirrors InvitationEmailService: invitation is part of user creation, so a misconfigured environment
        // should fail loudly at construction rather than silently drop invites.
        Guard.NotNullOrEmpty(settings.Notify.ApiKey, nameof(settings.Notify.ApiKey));
        Guard.NotNullOrEmpty(settings.Notify.DhscAdminInvitationTemplateId, nameof(settings.Notify.DhscAdminInvitationTemplateId));
        Guard.NotNullOrEmpty(settings.Notify.LaAdminInvitationTemplateId, nameof(settings.Notify.LaAdminInvitationTemplateId));
        Guard.NotNullOrEmpty(settings.Notify.LaUserInvitationTemplateId, nameof(settings.Notify.LaUserInvitationTemplateId));
        Guard.NotNullOrEmpty(settings.Notify.ClientUrl, nameof(settings.Notify.ClientUrl));
    }

    public Task<Either<Exception, Unit>> SendInvitationAsync(string email, Name name, UserRole role, CancellationToken cancellationToken)
    {
        // TemplateIdFor returns null rather than throwing: SendInvitationAsync is not itself async, so an
        // exception thrown here would surface synchronously from the call rather than inside the returned Task,
        // bypassing the Either<Exception, Unit> contract every caller relies on to log a warning and keep the
        // already-persisted user rather than fail user creation.
        var templateId = TemplateIdFor(role);
        if (templateId is null)
        {
            return Task.FromResult<Either<Exception, Unit>>(new UnsupportedUserRoleException(role.ToString()));
        }

        var personalisation = new Dictionary<string, dynamic>
        {
            { "Name", name.ToString() },
            { "login_url", Settings.ClientUrl },
        };

        return SendAsync(email, templateId, personalisation, "invitation", cancellationToken);
    }

    private string? TemplateIdFor(UserRole role)
    {
        return role.ToString() switch
        {
            Role.Admin => Settings.DhscAdminInvitationTemplateId,
            Role.OrganisationAdmin => Settings.LaAdminInvitationTemplateId,
            Role.Lead => Settings.LaUserInvitationTemplateId,
            _ => null,
        };
    }
}