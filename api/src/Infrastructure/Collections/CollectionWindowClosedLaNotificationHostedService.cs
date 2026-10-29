using Application.Common.Interfaces;
using Application.Common.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Collections;

/// <summary>
/// Decides when the LA window-closed-notification sweep runs (BLUM-8155).
///
/// Like CollectionClosureNotificationHostedService, a window closing is not an event anything observes —
/// CollectionStatus is derived, so nothing runs and nothing is written when a deadline passes. This one also has
/// to wait out the extra day before the notification is owed, so it re-checks that gate itself rather than firing
/// the moment a collection closes.
///
/// Modelled on CollectionClosureNotificationHostedService, and for the same reasons: it sweeps once as soon as
/// the app is up and then on a timer, because the App Service runs with always_on = false, so the site is
/// unloaded after roughly twenty minutes without traffic and a purely timer-driven job would simply stop.
///
/// Safe to run on several instances at once — App Service overlaps them during a deploy.
/// Collection.WindowClosedLaNotifiedAt is stamped inside the same scoped DbContext that read it, so the worst a
/// race can produce is one duplicate email per collection, never an endless resend.
/// </summary>
public class CollectionWindowClosedLaNotificationHostedService(
    IServiceScopeFactory scopeFactory,
    ApplicationSettings settings,
    ILogger<CollectionWindowClosedLaNotificationHostedService> logger)
    : BackgroundService
{
    // PeriodicTimer refuses any period beyond Timer.MaxSupportedTimeout (~49.7 days), and this value comes from an
    // App Service app setting a human types. A typo must not be able to crash-loop the host, so it is clamped.
    private const int MaxIntervalHours = 24 * 7;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await SweepAsync(stoppingToken);

        var configuredHours = settings.CollectionNotifications.WindowClosedLaSweepIntervalHours;
        if (configuredHours <= 0)
        {
            // Periodic sweeping switched off; the startup sweep above still ran.
            return;
        }

        var intervalHours = Math.Min(configuredHours, MaxIntervalHours);
        if (intervalHours != configuredHours)
        {
            logger.LogWarning(
                "WindowClosedLaSweepIntervalHours is {Configured}, which is beyond the supported maximum; sweeping every {Applied} hours instead.",
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
            logger.LogError(exception, "Collection window-closed LA notification scheduling stopped; sweeps will resume on the next restart.");
        }
    }

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        try
        {
            // CreateAsyncScope, not CreateScope: the notifier and the DbContext beneath it are scoped, and this
            // service is not. RepositoryBase implements IAsyncDisposable ONLY — see
            // CollectionClosureNotificationHostedService for why a sync scope Dispose would misreport every sweep
            // as failed.
            await using var scope = scopeFactory.CreateAsyncScope();
            var notifier = scope.ServiceProvider.GetRequiredService<ICollectionWindowClosedLaNotifier>();

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
            logger.LogWarning(exception, "Collection window-closed LA notification sweep failed; it will be retried on the next run.");
        }
    }
}