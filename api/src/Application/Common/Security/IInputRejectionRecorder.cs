namespace Application.Common.Security;

/// <summary>
/// Records a rejected request for security monitoring (BLUM-7240 AC8).
///
/// One interface for both tiers so the two places that reject input — ValidationExceptionMiddleware for
/// commands and query strings, InvalidModelStateResponse for model binding — emit through a single path and
/// cannot drift apart in what they report.
///
/// Implementations must never throw. This is called while a 400 is already being written; an audit failure
/// turning that into a 500 would be worse than the missing row.
/// </summary>
public interface IInputRejectionRecorder
{
    Task RecordAsync(InputRejection rejection, CancellationToken cancellationToken = default);
}