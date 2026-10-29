using Application.Common.Interfaces;
using Application.Common.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.AuditLogs;

/// <summary>
/// Decides when the audit retention sweep runs (BLUM-8750). A copy of MfaSessionRetentionHostedService, whose doc
/// explains each choice: sweep at startup because always_on = false makes cold starts the only reliable tick, then
/// on a clamped timer; never let anything escape, because an exception here stops the host; tolerate several
/// instances sweeping at once rather than take a lock, because the delete is idempotent.
/// </summary>
public class AuditLogRetentionHostedService(
    IServiceScopeFactory scopeFactory,
    ApplicationSettings settings,
    ILogger<AuditLogRetentionHostedService> logger)
    : BackgroundService
{
    // PeriodicTimer refuses a period beyond ~49.7 days, and a typed app setting must not crash-loop the API.
    private const int MaxIntervalHours = 24 * 7;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await SweepAsync(stoppingToken);

        var configuredHours = settings.Audit.RetentionSweepIntervalHours;
        if (configuredHours <= 0)
        {
            // Periodic sweeping switched off; the startup sweep above still ran.
            return;
        }

        var intervalHours = Math.Min(configuredHours, MaxIntervalHours);
        if (intervalHours != configuredHours)
        {
            logger.LogWarning(
                "Audit RetentionSweepIntervalHours is {Configured}, which is beyond the supported maximum; sweeping every {Applied} hours instead.",
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
            logger.LogError(exception, "Audit log retention scheduling stopped; sweeps will resume on the next restart.");
        }
    }

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Its own scope: the sweeper and the DbContext beneath it are scoped, and this service is not. Async,
            // unlike the MFA sweep's: AuditLogRepository is a RepositoryBase, which is only IAsyncDisposable, so a
            // synchronous scope throws on dispose — after the delete, where the catch below would hide it.
            await using var scope = scopeFactory.CreateAsyncScope();
            var sweeper = scope.ServiceProvider.GetRequiredService<IAuditLogRetentionSweeper>();

            await sweeper.SweepAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Audit log retention sweep failed; it will be retried on the next run.");
        }
    }
}