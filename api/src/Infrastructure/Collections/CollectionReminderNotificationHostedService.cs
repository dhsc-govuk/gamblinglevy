using Application.Common.Interfaces;
using Application.Common.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Collections;

/// <summary>
/// Decides when the deadline-reminder sweep runs (BLUM-7571).
///
/// A configured reminder's fire date arriving is not an event anything observes: collection_reminders holds an
/// OFFSET, not a date, and CollectionStatus is derived — so nothing runs and nothing is written when a reminder
/// falls due. Something has to go looking, which is what this does.
///
/// Modelled on CollectionClosureNotificationHostedService, and for the same reasons. It sweeps once as soon as
/// the app is up and then on a timer, and the startup sweep is not belt-and-braces: the App Service runs with
/// always_on = false, so the site is unloaded after roughly twenty minutes without traffic and a purely
/// timer-driven job would simply stop. Sweeping at startup means every cold start sweeps, and a cold start is
/// what happens the next time anybody uses the service. The consequence to be aware of is that there is no
/// wall-clock guarantee: a reminder can be several hours late, or go out at 03:00. Turning Always On on later
/// upgrades this to a real guarantee with no code change.
///
/// Safe to run on several instances at once — App Service overlaps them during a deploy. Unlike the closure
/// sweep, this one does not rely on that being harmless: it claims a row in collection_reminder_dispatches
/// BEFORE any email leaves, so the loser of a race is refused by the unique index rather than duplicating a
/// fan-out to every Local Authority in the country.
///
/// This is the second clone of the same startup-sweep-then-PeriodicTimer shape (the first being the closure
/// service above it; MfaSessionRetentionHostedService is a third variant that predates both). It is kept
/// separate rather than folded into the closure sweep because this one is an order of magnitude heavier —
/// hundreds of outbound Notify calls per tick against a handful — and sharing a tick would put the cheap,
/// latency-sensitive closure notification behind it and land a shutdown token mid-fan-out of the wrong job.
/// When a THIRD of these arrives (BLUM-7572's escalation sweep is the obvious candidate), extract a generic
/// PeriodicSweepHostedService&lt;TSweeper&gt; rather than cloning this again.
/// </summary>
public class CollectionReminderNotificationHostedService(
    IServiceScopeFactory scopeFactory,
    ApplicationSettings settings,
    ILogger<CollectionReminderNotificationHostedService> logger)
    : BackgroundService
{
    // PeriodicTimer refuses any period beyond Timer.MaxSupportedTimeout (~49.7 days), and this value comes from an
    // App Service app setting a human types. A typo must not be able to crash-loop the host, so it is clamped.
    private const int MaxIntervalHours = 24 * 7;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await SweepAsync(stoppingToken);

        var configuredHours = settings.CollectionNotifications.ReminderSweepIntervalHours;
        if (configuredHours <= 0)
        {
            // Periodic sweeping switched off; the startup sweep above still ran.
            return;
        }

        var intervalHours = Math.Min(configuredHours, MaxIntervalHours);
        if (intervalHours != configuredHours)
        {
            logger.LogWarning(
                "ReminderSweepIntervalHours is {Configured}, which is beyond the supported maximum; sweeping every {Applied} hours instead.",
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
            logger.LogError(exception, "Collection reminder scheduling stopped; sweeps will resume on the next restart.");
        }
    }

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Its own scope: the notifier and the DbContext beneath it are scoped, and this service is not.
            //
            // CreateAsyncScope, not CreateScope: the notifier pulls CollectionRepository, UserRepository and
            // CollectionReminderDispatchRepository, and RepositoryBase implements IAsyncDisposable ONLY. A sync
            // scope Dispose throws on the first such entry and abandons the rest of the disposal loop, so the
            // DbContext underneath leaks its pooled connection — and because the throw lands inside the catch
            // below, every single sweep would log as failed while actually having worked.
            await using var scope = scopeFactory.CreateAsyncScope();
            var notifier = scope.ServiceProvider.GetRequiredService<ICollectionReminderNotifier>();

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
            // a reminder being late. Log it and wait for the next tick.
            logger.LogWarning(exception, "Collection reminder sweep failed; it will be retried on the next run.");
        }
    }
}