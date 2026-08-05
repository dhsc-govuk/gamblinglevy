using Application.Common.Interfaces;
using Application.Common.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Mfa;

/// <summary>
/// Decides when the retention sweep runs (BLUM-7241). The only background worker in this API.
///
/// It sweeps once as soon as the app is up and then on a timer, and the first of those is not belt-and-braces
/// — it is what makes this work at all today. The App Service runs with always_on = false, so the site is
/// unloaded after roughly twenty minutes without traffic and a purely timer-driven job would simply stop.
/// Sweeping at startup means every cold start sweeps, and a cold start is exactly what happens the next time
/// anybody uses the service. Turning Always On on later upgrades this to a real wall-clock guarantee with no
/// code change; nothing here needs to know which way it is configured.
///
/// Safe to run on several instances at once — App Service overlaps them during a deploy. The delete is
/// idempotent, so a second instance sweeping the same rows simply deletes none of them. That matches how this
/// codebase already handles concurrent boot (see ApplicationDbContextInitialiser's unique-violation catch):
/// tolerate the race, do not take a lock.
/// </summary>
public class MfaSessionRetentionHostedService(
    IServiceScopeFactory scopeFactory,
    ApplicationSettings settings,
    ILogger<MfaSessionRetentionHostedService> logger)
    : BackgroundService
{
    // PeriodicTimer refuses any period beyond Timer.MaxSupportedTimeout (~49.7 days), and this value comes
    // from an App Service app setting a human types. "8760" for "once a year" is an entirely reasonable thing
    // to write and would otherwise throw straight out of ExecuteAsync — which, with the .NET 6+ default of
    // BackgroundServiceExceptionBehavior.StopHost, crash-loops the whole API seconds after every boot without
    // serving a request. A configuration typo must not be able to do that, so the value is clamped instead.
    private const int MaxIntervalHours = 24 * 7;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await SweepAsync(stoppingToken);

        var configuredHours = settings.Mfa.SessionRetentionSweepIntervalHours;
        if (configuredHours <= 0)
        {
            // Periodic sweeping switched off; the startup sweep above still ran.
            return;
        }

        var intervalHours = Math.Min(configuredHours, MaxIntervalHours);
        if (intervalHours != configuredHours)
        {
            logger.LogWarning(
                "SessionRetentionSweepIntervalHours is {Configured}, which is beyond the supported maximum; sweeping every {Applied} hours instead.",
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
            // The timer construction and the loop itself are inside the try for the same reason SweepAsync
            // swallows: nothing this service does is worth stopping the host for.
            logger.LogError(exception, "MFA session retention scheduling stopped; sweeps will resume on the next restart.");
        }
    }

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Its own scope: the sweeper and the DbContext beneath it are scoped, and this service is not.
            using var scope = scopeFactory.CreateScope();
            var sweeper = scope.ServiceProvider.GetRequiredService<IMfaSessionRetentionSweeper>();

            await sweeper.SweepAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Nothing may escape. Since .NET 6 an unhandled exception in a BackgroundService stops the entire
            // host by default, so a transient database blip during a retention sweep would take the API down
            // with it — a far worse outcome than the tidying being late. Log it and wait for the next tick.
            logger.LogWarning(exception, "MFA session retention sweep failed; it will be retried on the next run.");
        }
    }
}