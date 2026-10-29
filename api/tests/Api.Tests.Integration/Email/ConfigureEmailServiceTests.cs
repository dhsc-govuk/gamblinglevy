using Application.Common.Interfaces;
using Application.Common.Settings;
using FluentAssertions;
using Infrastructure.Email;
using Infrastructure.Notify;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Api.Tests.Integration.Email;

/// <summary>
/// Verifies ApplicationSettings.EmailProvider actually controls which concrete type
/// ConfigureEmailService.AddEmailServices() resolves for each email purpose. A wrong branch here would silently
/// send through the wrong provider with no compiler or runtime error, so it is asserted directly against the
/// composition root rather than inferred from behaviour. This is a plain DI-container test, not an HTTP-driven
/// scenario, so it does not inherit BaseIntegrationTest and needs no Testcontainers/database.
/// </summary>
public class ConfigureEmailServiceTests
{
    [Fact]
    public void AddEmailServices_ResolvesSendGridByDefault()
    {
        using var provider = BuildProvider(EmailProvider.SendGrid);

        provider.GetRequiredService<IInvitationEmailService>().Should().BeOfType<InvitationEmailService>();
        provider.GetRequiredService<ISubmissionConfirmationEmailService>().Should().BeOfType<SubmissionConfirmationEmailService>();
        provider.GetRequiredService<INewSignInEmailService>().Should().BeOfType<NewSignInEmailService>();
    }

    [Fact]
    public void AddEmailServices_ResolvesNotifyWhenSelected()
    {
        using var provider = BuildProvider(EmailProvider.Notify, notifyApiKey: ValidNotifyApiKey);

        provider.GetRequiredService<IInvitationEmailService>().Should().BeOfType<NotifyInvitationEmailService>();
        provider.GetRequiredService<ISubmissionConfirmationEmailService>().Should().BeOfType<NotifySubmissionConfirmationEmailService>();
        provider.GetRequiredService<INewSignInEmailService>().Should().BeOfType<NotifyNewSignInEmailService>();
    }

    /// <summary>
    /// With EmailProvider=Notify and the committed empty Notify:ApiKey (appsettings.json), resolving
    /// ISubmissionConfirmationEmailService/INewSignInEmailService must not throw — those two deliberately check
    /// config at send time and return a Left instead, because they sit on the submit and post-sign-in paths where
    /// a 500 is the wrong failure mode. NotifyInvitationEmailService is the deliberate exception: it fails loudly
    /// at construction, same as InvitationEmailService, because invitation is part of user creation and a
    /// misconfigured environment should not silently drop invites.
    /// </summary>
    [Fact]
    public void AddEmailServices_DoesNotThrowResolvingSendTimeGuardedNotifyServices_WhenApiKeyIsMissing()
    {
        using var provider = BuildProvider(EmailProvider.Notify, notifyApiKey: string.Empty);

        provider.GetRequiredService<ISubmissionConfirmationEmailService>().Should().BeOfType<NotifySubmissionConfirmationEmailService>();
        provider.GetRequiredService<INewSignInEmailService>().Should().BeOfType<NotifyNewSignInEmailService>();

        var act = () => provider.GetRequiredService<IInvitationEmailService>();
        act.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// The closure notification (BLUM-7719) is the one purpose with no SendGrid twin — it exists only as a Notify
    /// template — so it is registered outside the EmailProvider switch and must resolve the same either way. It also
    /// must not throw on an empty key: it is resolved inside CloseCollectionCommandHandler, where a throw would 500
    /// a close that has already been committed, and inside the background sweep's scope, where it would stop the
    /// sweep running at all. Missing config is a logged no-send instead — which is the state it ships in.
    /// </summary>
    [Theory]
    [InlineData(EmailProvider.SendGrid)]
    [InlineData(EmailProvider.Notify)]
    public void AddEmailServices_AlwaysResolvesTheNotifyCollectionClosedService(EmailProvider emailProvider)
    {
        using var provider = BuildProvider(emailProvider, notifyApiKey: string.Empty);

        provider.GetRequiredService<ICollectionClosedEmailService>()
            .Should().BeOfType<NotifyCollectionClosedEmailService>();
    }

    /// <summary>
    /// The two BLUM-7571 reminder emails are the same shape as the closure notification: Notify-only, no SendGrid
    /// twin, registered outside the EmailProvider switch, and resolved both inside SendManualReminderCommandHandler
    /// and inside the background sweep's own scope — so neither may throw on an empty key. Note that the test host
    /// itself runs EmailProvider SendGrid, which is exactly the branch that would break if these were moved inside
    /// the switch.
    /// </summary>
    [Theory]
    [InlineData(EmailProvider.SendGrid)]
    [InlineData(EmailProvider.Notify)]
    public void AddEmailServices_AlwaysResolvesTheNotifyReminderServices(EmailProvider emailProvider)
    {
        using var provider = BuildProvider(emailProvider, notifyApiKey: string.Empty);

        provider.GetRequiredService<ICollectionReminderEmailService>()
            .Should().BeOfType<NotifyCollectionReminderEmailService>();

        provider.GetRequiredService<ICollectionReminderSummaryEmailService>()
            .Should().BeOfType<NotifyCollectionReminderSummaryEmailService>();
    }

    /// <summary>
    /// And the two BLUM-7721 window-opened services, for the same reasons: Notify-only with no SendGrid twin, so
    /// they sit outside the EmailProvider switch, and they are resolved inside the background sweep's own DI
    /// scope — where a constructor throw on an empty API key would be swallowed and the sweep would silently
    /// never run, taking the deadline reminders down with it.
    /// </summary>
    [Theory]
    [InlineData(EmailProvider.SendGrid)]
    [InlineData(EmailProvider.Notify)]
    public void AddEmailServices_AlwaysResolvesTheNotifyWindowOpenedServices(EmailProvider emailProvider)
    {
        using var provider = BuildProvider(emailProvider, notifyApiKey: string.Empty);

        provider.GetRequiredService<ICollectionWindowOpenedEmailService>()
            .Should().BeOfType<NotifyCollectionWindowOpenedEmailService>();

        provider.GetRequiredService<ICollectionWindowOpenedSummaryEmailService>()
            .Should().BeOfType<NotifyCollectionWindowOpenedSummaryEmailService>();
    }

    private const string ValidNotifyApiKey =
        "test-notify-key-00000000-0000-0000-0000-000000000000-11111111-1111-1111-1111-111111111111";

    private static ServiceProvider BuildProvider(EmailProvider emailProvider, string notifyApiKey = ValidNotifyApiKey)
    {
        var settings = new ApplicationSettings
        {
            EmailProvider = emailProvider,
            SendGrid = new SendGridSettings
            {
                ClientUrl = "http://localhost:3000",
                EmailApiKey = "SG.test",
                SenderEmail = "no-reply@example.com",
            },
            Notify = new NotifySettings
            {
                // NotificationClient validates a non-empty key's shape (name-serviceId-apiKeyId) at construction,
                // even though this test never makes a real call — an arbitrary non-empty string throws
                // NotifyAuthException before the DI branch under test is even reached.
                ApiKey = notifyApiKey,
                ClientUrl = "http://localhost:3000",
                DhscAdminInvitationTemplateId = "00000000-0000-0000-0000-000000000000",
                LaAdminInvitationTemplateId = "00000000-0000-0000-0000-000000000000",
                LaUserInvitationTemplateId = "00000000-0000-0000-0000-000000000000",
                SubmissionConfirmationTemplateId = "00000000-0000-0000-0000-000000000000",
                NewSignInTemplateId = "00000000-0000-0000-0000-000000000000",
                CollectionClosedTemplateId = "00000000-0000-0000-0000-000000000000",
                CollectionReminderTemplateId = "00000000-0000-0000-0000-000000000000",
                CollectionReminderSummaryTemplateId = "00000000-0000-0000-0000-000000000000",
                CollectionWindowOpenedTemplateId = "00000000-0000-0000-0000-000000000000",
                CollectionWindowOpenedSummaryTemplateId = "00000000-0000-0000-0000-000000000000",
            },
        };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(settings);
        services.AddEmailServices();

        return services.BuildServiceProvider();
    }
}