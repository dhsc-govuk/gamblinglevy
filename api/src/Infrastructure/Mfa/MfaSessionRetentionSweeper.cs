using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Application.Common.Settings;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Mfa;

public class MfaSessionRetentionSweeper(
    IMfaSessionRepository sessionRepository,
    IDateTimeProvider dateTimeProvider,
    ApplicationSettings settings,
    ILogger<MfaSessionRetentionSweeper> logger)
    : IMfaSessionRetentionSweeper
{
    // A backlog is cleared over several bounded deletes rather than one, but not without end: if something
    // keeps handing back full batches — a clock jumping backwards, a cutoff misconfigured to the future —
    // this stops rather than deleting the table one batch at a time. Whatever is left waits for the next run.
    private const int MaxBatchesPerSweep = 20;

    public async Task<int> SweepAsync(CancellationToken cancellationToken = default)
    {
        var retentionDays = settings.Mfa.SessionRetentionDays;
        if (retentionDays <= 0)
        {
            return 0;
        }

        var batchSize = Math.Max(1, settings.Mfa.SessionRetentionSweepBatchSize);
        var endedBefore = dateTimeProvider.GetUtcNow().AddDays(-retentionDays);

        var deleted = 0;
        for (var batch = 0; batch < MaxBatchesPerSweep; batch++)
        {
            var result = await sessionRepository.DeleteEndedSessionsAsync(endedBefore, batchSize, cancellationToken);
            deleted += result.Deleted;

            // Judged on what the query FOUND, never on what this call managed to delete. A second instance
            // sweeping alongside this one can remove some of the same rows first, so Deleted comes back short
            // while the backlog is untouched — looping on it would read a concurrent sweep as "nothing left"
            // and abandon exactly the large first-run backlog this loop exists to clear.
            if (result.Found < batchSize)
            {
                break;
            }
        }

        if (deleted > 0)
        {
            logger.LogInformation(
                "Deleted {Count} MFA session record(s) that ended before {Cutoff:u}, per the {RetentionDays}-day retention period.",
                deleted,
                endedBefore,
                retentionDays);
        }

        return deleted;
    }
}