using System.Text.Json;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Application.Common.Settings;
using Domain.AuditLogs;
using Domain.Users;
using Microsoft.Extensions.Logging;

namespace Infrastructure.AuditLogs;

public class AuditLogRetentionSweeper(
    IAuditLogRepository auditLogRepository,
    IDateTimeProvider dateTimeProvider,
    ApplicationSettings settings,
    ILogger<AuditLogRetentionSweeper> logger)
    : IAuditLogRetentionSweeper
{
    // Clears a backlog over several bounded deletes, but not without end, for the reason MfaSessionRetentionSweeper
    // gives: a clock jumping or a misconfigured cutoff must not delete the table one batch at a time in one run.
    private const int MaxBatchesPerSweep = 20;

    public async Task<int> SweepAsync(CancellationToken cancellationToken = default)
    {
        var retentionDays = settings.Audit.RetentionDays;
        if (retentionDays <= 0)
        {
            return 0;
        }

        var batchSize = Math.Max(1, settings.Audit.RetentionSweepBatchSize);
        var cutoff = dateTimeProvider.GetUtcNow().AddDays(-retentionDays);

        var deleted = 0;
        for (var batch = 0; batch < MaxBatchesPerSweep; batch++)
        {
            var result = await auditLogRepository.DeleteOccurredBeforeAsync(cutoff, batchSize, cancellationToken);
            deleted += result.Deleted;

            // Judged on what the query found, never on what this call deleted: a second instance sweeping alongside
            // this one takes some of the same rows, and looping on Deleted would read that as "nothing left".
            if (result.Found < batchSize)
            {
                break;
            }
        }

        if (deleted > 0)
        {
            logger.LogInformation(
                "Deleted {Count} audit log row(s) that occurred before {Cutoff:u}, per the {RetentionDays}-day retention period.",
                deleted,
                cutoff,
                retentionDays);

            // After the deletes and outside any transaction, so TryAppendAsync: a failure to record the purge is
            // logged and must not be mistaken for a failed sweep. It is the newest row in the table, so the next
            // sweep never takes it with the rows it describes.
            await auditLogRepository.TryAppendAsync(AuditLog.Create(
                UserId.Empty(),
                AuditTargetType.System,
                Guid.Empty,
                AuditAction.SystemAuditLogsPurged,
                before: null,
                after: JsonSerializer.Serialize(new { cutoff, retentionDays, deleted })));
        }

        return deleted;
    }
}