namespace Application.Common.Interfaces;

/// <summary>
/// Deletes audit_logs rows older than AuditSettings.RetentionDays, whatever their action (BLUM-8750).
///
/// Separate from the thing that decides *when* to run it, as IMfaSessionRetentionSweeper is, so a test can drive
/// one sweep directly without a host, a timer or a wait.
/// </summary>
public interface IAuditLogRetentionSweeper
{
    /// <summary>Runs one sweep to completion and returns how many rows were deleted.</summary>
    Task<int> SweepAsync(CancellationToken cancellationToken = default);
}