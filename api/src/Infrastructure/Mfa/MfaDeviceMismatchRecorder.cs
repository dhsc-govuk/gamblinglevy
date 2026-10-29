using System.Globalization;
using System.Text.Json;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Application.Common.Settings;
using Application.Common.Telemetry;
using Domain.AuditLogs;
using Infrastructure.Security;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Mfa;

/// <summary>
/// The record of a session refused on its IP / User-Agent binding (BLUM-8321).
///
/// DAC users were being sent back to the code-entry screen every few minutes mid-task, and the refusal behind
/// it — MfaSessionStatus.DeviceMismatch, answered as 403 MFA_REQUIRED — wrote nothing anywhere, so there was
/// no way to tell from the server whether their address, their browser or something else entirely had moved.
/// This changes nothing about the decision; it only makes it visible.
///
/// <b>Every mismatch</b> emits the mfa_session_device_mismatch telemetry event and one Warning log line.
/// Neither touches the database, so the true rate stays visible in Application Insights, and under Sentry's
/// defaults a Warning is a breadcrumb rather than an alert.
///
/// <b>The audit row is throttled per user</b> through AuditFloodThrottle. The address is read from a header
/// the caller can set, so without a cap a signed-in user could make the service commit one audit row per
/// request just by varying it.
///
/// <b>A tolerated address change</b> (BLUM-7848) goes through the same path as mfa.session.ip_changed, with
/// its own throttle scope and telemetry event and an Information rather than Warning log line — the request
/// was allowed, so it is evidence, not a failure.
/// </summary>
public class MfaDeviceMismatchRecorder(
    IAuditLogRepository auditLogRepository,
    TelemetryClient telemetryClient,
    AuditFloodThrottle throttle,
    ApplicationSettings settings,
    ILogger<MfaDeviceMismatchRecorder> logger)
    : IMfaDeviceMismatchRecorder
{
    /// <summary>Namespaces this trail's counters so it cannot spend the rate-limit or input-validation budgets.</summary>
    public const string ThrottleScope = "mfa_device_mismatch_audit";

    /// <summary>
    /// Separate from <see cref="ThrottleScope"/> so that a user behind a proxy pool, whose address changes on
    /// most requests, cannot spend the budget that records an actual refusal.
    /// </summary>
    public const string IpChangeThrottleScope = "mfa_ip_change_audit";

    private const string TelemetryEventName = "mfa_session_device_mismatch";
    private const string IpChangeTelemetryEventName = "mfa_session_ip_changed";

    private static readonly TimeSpan AuditWindow = TimeSpan.FromHours(1);

    public Task RecordAsync(MfaDeviceMismatch mismatch) =>
        RecordCoreAsync(mismatch, refused: true);

    public Task RecordToleratedIpChangeAsync(MfaDeviceMismatch change) =>
        RecordCoreAsync(change, refused: false);

    private async Task RecordCoreAsync(MfaDeviceMismatch mismatch, bool refused)
    {
        try
        {
            EmitTelemetry(mismatch, refused ? TelemetryEventName : IpChangeTelemetryEventName);
            EmitLog(mismatch, refused);

            var decision = throttle.Reserve(
                refused ? ThrottleScope : IpChangeThrottleScope,
                mismatch.UserId.Value.ToString(),
                new AuditThrottleLimits(
                    settings.Mfa.MaxDeviceMismatchAuditRowsPerUserPerHour,
                    settings.Mfa.MaxDeviceMismatchAuditRowsPerHour,
                    AuditWindow));

            if (!decision.Admitted)
            {
                return;
            }

            // Not a request token: a caller that drops the socket as the 403 goes out would otherwise cancel
            // the write that records why they got it.
            await auditLogRepository.AppendAsync(
                AuditLog.Create(
                    mismatch.UserId,
                    AuditTargetType.MfaSession,
                    mismatch.SessionId.Value,
                    refused ? AuditAction.MfaSessionDeviceMismatch : AuditAction.MfaSessionIpChanged,
                    before: SerialiseBefore(mismatch),
                    after: SerialiseAfter(mismatch, decision.SuppressedSincePrevious)),
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            // Deliberately swallowed. The answer — a 403, or letting the request through — is already decided,
            // and a failure to record it must not turn it into a 500.
            logger.LogError(exception, "Failed to record MFA session device binding change for session {SessionId}", mismatch.SessionId.Value);
        }
    }

    private static string SerialiseBefore(MfaDeviceMismatch mismatch)
    {
        return JsonSerializer.Serialize(new
        {
            ip = mismatch.RecordedIpAddress,
            device = mismatch.RecordedDeviceLabel,
        });
    }

    private static string SerialiseAfter(MfaDeviceMismatch mismatch, int suppressed)
    {
        return JsonSerializer.Serialize(new
        {
            ip = mismatch.CurrentIpAddress,
            device = mismatch.CurrentDeviceLabel,
            ipMatches = mismatch.IpAddressMatches,
            userAgentMatches = mismatch.UserAgentMatches,
            sessionAgeSeconds = mismatch.SessionAgeSeconds,
            idleSeconds = mismatch.IdleSeconds,
            suppressedSincePrevious = suppressed,
        });
    }

    private void EmitTelemetry(MfaDeviceMismatch mismatch, string eventName)
    {
        telemetryClient.TrackEventIfEnabled(
            eventName,
            new Dictionary<string, string>
            {
                ["sessionId"] = mismatch.SessionId.Value.ToString(),
                ["userId"] = mismatch.UserId.Value.ToString(),
                ["recordedIp"] = mismatch.RecordedIpAddress,
                ["currentIp"] = mismatch.CurrentIpAddress,
                ["recordedDevice"] = mismatch.RecordedDeviceLabel,
                ["currentDevice"] = mismatch.CurrentDeviceLabel,
                ["ipMatches"] = mismatch.IpAddressMatches.ToString(),
                ["userAgentMatches"] = mismatch.UserAgentMatches.ToString(),
                ["sessionAgeSeconds"] = mismatch.SessionAgeSeconds.ToString(CultureInfo.InvariantCulture),
                ["idleSeconds"] = mismatch.IdleSeconds.ToString(CultureInfo.InvariantCulture),
            });
    }

    private void EmitLog(MfaDeviceMismatch mismatch, bool refused)
    {
        // A fixed template so the message text is queryable, and nothing the caller controls is interpolated:
        // both addresses have been through IpAddressNormaliser and both devices are DeviceLabelResolver labels.
        if (!refused)
        {
            logger.LogInformation(
                "MFA session {SessionId} allowed after an IP change for user {UserId}: ip {RecordedIp} -> {CurrentIp}, device {CurrentDevice}; session age {SessionAgeSeconds}s, idle {IdleSeconds}s",
                mismatch.SessionId.Value,
                mismatch.UserId.Value,
                mismatch.RecordedIpAddress,
                mismatch.CurrentIpAddress,
                mismatch.CurrentDeviceLabel,
                mismatch.SessionAgeSeconds,
                mismatch.IdleSeconds);
            return;
        }

        logger.LogWarning(
            "MFA session {SessionId} refused on its device binding for user {UserId}: ip {RecordedIp} -> {CurrentIp} (matches {IpMatches}), device {RecordedDevice} -> {CurrentDevice} (matches {UserAgentMatches}); session age {SessionAgeSeconds}s, idle {IdleSeconds}s",
            mismatch.SessionId.Value,
            mismatch.UserId.Value,
            mismatch.RecordedIpAddress,
            mismatch.CurrentIpAddress,
            mismatch.IpAddressMatches,
            mismatch.RecordedDeviceLabel,
            mismatch.CurrentDeviceLabel,
            mismatch.UserAgentMatches,
            mismatch.SessionAgeSeconds,
            mismatch.IdleSeconds);
    }
}