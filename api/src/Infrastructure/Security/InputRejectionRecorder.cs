using System.Text.Json;
using Application.Common.Interfaces.Repositories;
using Application.Common.Security;
using Application.Common.Settings;
using Application.Common.Telemetry;
using Domain.AuditLogs;
using Domain.Users;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Security;

/// <summary>
/// The two-tier security trail for rejected input (BLUM-7240 AC8).
///
/// Every rejection emits the validation_failed telemetry event and a structured log line with a stable
/// message template, so a Log Analytics alert can be built on it. This is the <em>only</em> producer of that
/// event: ValidationBehaviour used to emit it too, for commands alone, which meant one command failure
/// produced two events under one name with two different property bags — double-counting anything measuring
/// the volume and splitting any breakdown by the dimension only one of them carried.
///
/// Only a Security-tier rejection also writes an audit_logs row, and only if InputRejectionThrottle admits
/// it. See AuditAction.SecurityInputRejected for why every failure does not.
/// </summary>
public class InputRejectionRecorder(
    IAuditLogRepository auditLogRepository,
    TelemetryClient telemetryClient,
    InputRejectionThrottle throttle,
    ApplicationSettings settings,
    ILogger<InputRejectionRecorder> logger)
    : IInputRejectionRecorder
{
    private const string TelemetryEventName = "validation_failed";

    public async Task RecordAsync(InputRejection rejection, CancellationToken cancellationToken = default)
    {
        try
        {
            var tier = rejection.Tier;

            EmitTelemetry(rejection, tier);
            EmitLog(rejection, tier);

            if (tier != InputRejectionTier.Security || !settings.InputValidation.AuditSecurityRejections)
            {
                return;
            }

            var decision = throttle.Reserve(ThrottleKey(rejection), settings.InputValidation);
            if (!decision.Admitted)
            {
                return;
            }

            // Deliberately not the request's cancellation token. A caller that sends a hostile payload and
            // closes the socket without reading the response — ordinary scanner behaviour — would otherwise
            // cancel this write and erase their own audit trail on demand.
            await auditLogRepository.AppendAsync(
                AuditLog.Create(
                    rejection.ActorId ?? UserId.Empty(),
                    AuditTargetType.Request,
                    rejection.RejectionId,
                    AuditAction.SecurityInputRejected,
                    before: null,
                    after: Serialise(rejection, decision.SuppressedSincePrevious)),
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            // Deliberately swallowed. This runs while a 400 is on its way to the caller, so letting an audit
            // failure escape would turn a correct rejection into a 500.
            logger.LogError(exception, "Failed to record input rejection {RejectionId}", rejection.RejectionId);
        }
    }

    /// <summary>
    /// Who the cap applies to. The endpoint is already a route template rather than a path, so this cannot
    /// be widened by varying a route value; an unauthenticated caller is bucketed by normalised IP.
    /// </summary>
    private static string ThrottleKey(InputRejection rejection)
    {
        return $"{rejection.ActorId?.Value.ToString() ?? rejection.IpAddress}_{rejection.Endpoint}";
    }

    private void EmitTelemetry(InputRejection rejection, InputRejectionTier tier)
    {
        foreach (var reason in rejection.Reasons)
        {
            telemetryClient.TrackEventIfEnabled(
                TelemetryEventName,
                new Dictionary<string, string>
                {
                    ["endpoint"] = rejection.Endpoint,
                    ["field"] = reason.Field,
                    ["rule"] = reason.Kind,
                    ["tier"] = tier.ToString(),
                    ["userId"] = rejection.ActorId?.Value.ToString() ?? string.Empty,
                    ["rejectionId"] = rejection.RejectionId.ToString(),
                });
        }
    }

    private void EmitLog(InputRejection rejection, InputRejectionTier tier)
    {
        // One line per rejection rather than per reason, and a fixed template so the message text is
        // queryable. Nothing the caller controls is interpolated: the endpoint is a route template, the IP
        // is re-rendered by IpAddressNormaliser, and the field names are constrained to a property shape by
        // InvalidModelStateResponse.
        var message = "Input rejected: {Tier} at {Endpoint} for {ActorId} from {IpAddress} ({DeviceLabel}); reasons {Reasons}; rejectionId {RejectionId}";
        var reasons = string.Join(", ", rejection.Reasons.Select(reason => $"{reason.Field}:{reason.Kind}"));

        if (tier == InputRejectionTier.Security)
        {
            logger.LogWarning(
                message,
                tier,
                rejection.Endpoint,
                rejection.ActorId?.Value,
                rejection.IpAddress,
                rejection.DeviceLabel,
                reasons,
                rejection.RejectionId);
            return;
        }

        logger.LogInformation(
            message,
            tier,
            rejection.Endpoint,
            rejection.ActorId?.Value,
            rejection.IpAddress,
            rejection.DeviceLabel,
            reasons,
            rejection.RejectionId);
    }

    private static string Serialise(InputRejection rejection, int suppressed)
    {
        return JsonSerializer.Serialize(new
        {
            rejectionId = rejection.RejectionId,
            endpoint = rejection.Endpoint,
            reasons = rejection.Reasons.Select(reason => new { field = reason.Field, kind = reason.Kind }),
            ip = rejection.IpAddress,
            device = rejection.DeviceLabel,
            actor = rejection.ActorId is null ? "unauthenticated" : "authenticated",
            suppressedSincePrevious = suppressed,
        });
    }
}