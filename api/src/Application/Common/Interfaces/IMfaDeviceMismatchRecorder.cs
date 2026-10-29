using Domain.Users;

namespace Application.Common.Interfaces;

/// <summary>
/// Puts a session refused on its device binding on the record (BLUM-8321).
///
/// Implementations must never throw. This runs while a 403 MFA_REQUIRED is already on its way to the caller,
/// and turning a refusal the gate meant to send into a 500 it did not is strictly worse than losing the
/// record — the same contract IRateLimitEventRecorder carries.
/// </summary>
public interface IMfaDeviceMismatchRecorder
{
    Task RecordAsync(MfaDeviceMismatch mismatch);

    /// <summary>
    /// Records a session that was <b>let through</b> although its IP address moved, because the browser still
    /// matched and MfaSettings.BindSessionToIpAddress is off (BLUM-7848). Same evidence as a refusal, written
    /// as mfa.session.ip_changed on its own throttle budget. Never throws, for the same reason: the request is
    /// already allowed, and failing to record that must not fail it.
    /// </summary>
    Task RecordToleratedIpChangeAsync(MfaDeviceMismatch change);
}

/// <summary>
/// A session that was live on both windows but whose request no longer matched the device it was bound to —
/// refused, or (for an address change alone) tolerated. "Recorded" is what the session was bound to at sign-in; "Current" is what this request
/// presented.
/// </summary>
/// <param name="RecordedIpAddress">mfa_sessions.ip_address — already normalised when the session was created.</param>
/// <param name="CurrentIpAddress">This request's address, through IpAddressNormaliser, never the raw header.</param>
/// <param name="RecordedDeviceLabel">mfa_sessions.device_label.</param>
/// <param name="CurrentDeviceLabel">DeviceLabelResolver's label for this request, never the raw User-Agent.</param>
/// <param name="IpAddressMatches">
/// Judged on the hashes, which is what the binding compares. Deliberately not derived from the two addresses
/// above: those are normalised, so a header that differs only in spelling or port shows the same address on
/// both sides while the binding still refuses — and telling that case apart is half the point.
/// </param>
/// <param name="UserAgentMatches">Judged on the hashes, for the same reason.</param>
/// <param name="SessionAgeSeconds">How long ago the session was created.</param>
/// <param name="IdleSeconds">
/// Seconds since mfa_sessions.last_activity_at. That column is only written back once it is at least a minute
/// stale (MfaSessionService.ActivityWriteThresholdSeconds), so this can overstate the true gap since the
/// last request by up to 60 seconds — precise enough to tell "after a pause" from "mid-session", not more.
/// </param>
public record MfaDeviceMismatch(
    MfaSessionId SessionId,
    UserId UserId,
    string RecordedIpAddress,
    string CurrentIpAddress,
    string RecordedDeviceLabel,
    string CurrentDeviceLabel,
    bool IpAddressMatches,
    bool UserAgentMatches,
    long SessionAgeSeconds,
    long IdleSeconds);