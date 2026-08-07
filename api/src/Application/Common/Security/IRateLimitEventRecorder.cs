namespace Application.Common.Security;

/// <summary>
/// Puts a rate-limit rejection on the security trail (BLUM-7239 AC6).
///
/// Implementations must never throw. This is called while a 429 is already on its way to the caller, and
/// turning a request the service deliberately refused into a 500 it did not mean to send is strictly worse
/// than losing the record — the same contract IInputRejectionRecorder carries, for the same reason.
/// </summary>
public interface IRateLimitEventRecorder
{
    Task RecordAsync(RateLimitEvent rateLimitEvent, CancellationToken cancellationToken = default);
}