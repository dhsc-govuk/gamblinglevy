namespace Application.Common.Interfaces;

/// <summary>
/// Deletes MFA session records that have been finished for longer than MfaSettings.SessionRetentionDays,
/// whoever they belong to (BLUM-7241).
///
/// Separate from the thing that decides *when* to run it so the work can be driven directly — by a test, or
/// later by anything else — without a host, a timer, or a wait.
/// </summary>
public interface IMfaSessionRetentionSweeper
{
    /// <summary>Runs one sweep to completion and returns how many records were deleted.</summary>
    Task<int> SweepAsync(CancellationToken cancellationToken = default);
}