using Application.Common.Interfaces;
using Application.Common.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Collections;

/// <summary>
/// Decides when the closure-notification sweep runs (BLUM-7719).
///
/// A data-collection window normally closes because its submission deadline passed, and that is not an event
/// anything observes: CollectionStatus is derived, so nothing runs and nothing is written — Collection.GetStatus()
/// simply starts answering Closed. Something therefore has to go looking, which is what this does.
///
/// Modelled on MfaSessionRetentionHostedService, and for the same reasons. It sweeps once as soon as the app is up
/// and then on a timer, and the startup sweep is not belt-and-braces: the App Service runs with always_on = false,
/// so the site is unloaded after roughly twenty minutes without traffic and a purely timer-driven job would simply
/// stop. Sweeping at startup means every cold start sweeps, and a cold start is what happens the next time anybody
/// uses the service. Turning Always On on later upgrades this to a real wall-clock guarantee with no code change.
///
/// Safe to run on several instances at once — App Service overlaps them during a deploy. Collection.ClosureNotifiedAt
/// is stamped inside the same scoped DbContext that read it, so the worst a race can produce is one duplicate email
/// per collection, never an endless resend.
/// </summary>
public class CollectionClosureNotificationHostedService(
    IServiceScopeFactory scopeFactory,
    ApplicationSettings settings,
    ILogger<CollectionClosureNotificationHostedService> logger)
    : BackgroundService
{
    // PeriodicTimer refuses any period beyond Timer.MaxSupportedTimeout (~49.7 days), and this value comes from an
    // App Service app setting a human types. A typo must not be able to crash-loop the host, so it is clamped.
    private const int MaxIntervalHours = 24 * 7;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await SweepAsync(stoppingToken);

        var configuredHours = settings.CollectionNotifications.ClosureSweepIntervalHours;
        if (configuredHours <= 0)
        {
            // Periodic sweeping switched off; the startup sweep above still ran.
            return;
        }

        var intervalHours = Math.Min(configuredHours, MaxIntervalHours);
        if (intervalHours != configuredHours)
        {
            logger.LogWarning(
                "ClosureSweepIntervalHours is {Configured}, which is beyond the supported maximum; sweeping every {Applied} hours instead.",
                configuredHours,
                intervalHours);
        }

        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromHours(intervalHours));

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await SweepAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down. Not a failure.
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Collection closure notification scheduling stopped; sweeps will resume on the next restart.");
        }
    }

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Its own scope: the notifier and the DbContext beneath it are scoped, and this service is not.
            //
            // CreateAsyncScope, not CreateScope: the notifier pulls CollectionRepository and UserRepository,
            // and RepositoryBase implements IAsyncDisposable ONLY. A sync scope Dispose throws on the first
            // such entry and abandons the rest of the disposal loop, so the DbContext underneath leaks its
            // pooled connection — and because the throw lands inside the catch below, every single sweep
            // would log as failed while actually having worked. MfaSessionRetentionHostedService gets away
            // with CreateScope only because MfaSessionRepository is not a RepositoryBase.
            await using var scope = scopeFactory.CreateAsyncScope();
            var notifier = scope.ServiceProvider.GetRequiredService<ICollectionClosureNotifier>();

            await notifier.SweepAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Nothing may escape. Since .NET 6 an unhandled exception in a BackgroundService stops the entire host
            // by default, so a transient database or Notify blip would take the API down with it — far worse than
            // a notification being late. Log it and wait for the next tick.
            logger.LogWarning(exception, "Collection closure notification sweep failed; it will be retried on the next run.");
        }
    }
}