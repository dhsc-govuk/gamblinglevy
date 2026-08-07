using System.Text.Json;
using Application.Common.Interfaces.Repositories;
using Application.Common.Security;
using Application.Common.Settings;
using Application.Common.Telemetry;
using Domain.AuditLogs;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Security;

/// <summary>
/// The security trail for rate-limit rejections (BLUM-7239 AC6).
///
/// Three stages, because a flood is the literal scenario and each stage bounds a different shape of it.
///
/// <b>Every rejection</b> emits the rate_limit_exceeded telemetry event and one structured log line at
/// Warning. Neither touches the database, so the true volume stays visible in Application Insights however
/// large the burst — and under Sentry's defaults a Warning becomes a breadcrumb rather than an alert, which
/// is what stops a flood paging somebody ten thousand times.
///
/// <b>The first rejection in a partition's window</b> is the only audit candidate. A ten-thousand-request
/// burst from one address against one route therefore proposes one row a window rather than ten thousand.
/// This costs nothing: the limiter already knows the number.
///
/// <b>AuditFloodThrottle</b> then applies the per-actor and global caps. Stage two cannot bound a burst
/// spread across many addresses, because every fresh address is a fresh "first rejection"; this can, and it
/// carries the suppressed tally onto the next admitted row so the spread stays visible as a number.
///
/// The single LogError sits after all three, and only there. Sentry's default MinimumEventLevel is Error, so
/// that line — and only that line — becomes an alertable event. "Alerting on sustained abuse" therefore
/// falls out of the existing telemetry stack with no Sentry-specific API and no new dependency: Warning for
/// volume, Error for escalation.
/// </summary>
public class RateLimitEventRecorder(
    IAuditLogRepository auditLogRepository,
    TelemetryClient telemetryClient,
    AuditFloodThrottle throttle,
    ApplicationSettings settings,
    ILogger<RateLimitEventRecorder> logger)
    : IRateLimitEventRecorder
{
    /// <summary>Namespaces this trail's counters so a flood here cannot blank the input-validation trail.</summary>
    public const string ThrottleScope = "rate_limit_audit";

    private const string TelemetryEventName = "rate_limit_exceeded";

    private static readonly TimeSpan AuditWindow = TimeSpan.FromHours(1);

    public async Task RecordAsync(RateLimitEvent rateLimitEvent, CancellationToken cancellationToken = default)
    {
        try
        {
            this.EmitTelemetry(rateLimitEvent);
            this.EmitLog(rateLimitEvent);

            if (!settings.RateLimiting.AuditRateLimitEvents)
            {
                return;
            }

            // Only the first rejection of the window. Every later one in the same window is the same burst
            // continuing, already counted onto this row as RejectionsInWindow.
            if (rateLimitEvent.RejectionsInWindow != 1)
            {
                return;
            }

            var decision = throttle.Reserve(
                ThrottleScope,
                ThrottleKey(rateLimitEvent),
                new AuditThrottleLimits(
                    settings.RateLimiting.MaxAuditRowsPerActorPerHour,
                    settings.RateLimiting.MaxAuditRowsPerHour,
                    AuditWindow));

            if (!decision.Admitted)
            {
                return;
            }

            logger.LogError(
                "Sustained rate-limit abuse: {Policy} at {Endpoint} from {IpAddress} ({DeviceLabel}); {RejectionsInWindow} rejected this window, {SuppressedSincePrevious} bursts suppressed since the previous record; eventId {EventId}",
                rateLimitEvent.Policy,
                rateLimitEvent.Endpoint,
                rateLimitEvent.IpAddress,
                rateLimitEvent.DeviceLabel,
                rateLimitEvent.RejectionsInWindow,
                decision.SuppressedSincePrevious,
                rateLimitEvent.EventId);

            // Deliberately not the request's cancellation token, for the reason InputRejectionRecorder gives:
            // a caller that floods and drops the socket without reading the response — ordinary scanner
            // behaviour, and the cheapest way to flood — would otherwise cancel this write and erase their
            // own audit trail on demand.
            await auditLogRepository.AppendAsync(
                AuditLog.Create(
                    RateLimitEvent.Actor,
                    AuditTargetType.Request,
                    rateLimitEvent.EventId,
                    AuditAction.SecurityRateLimited,
                    before: null,
                    after: Serialise(rateLimitEvent, decision.SuppressedSincePrevious)),
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            // Deliberately swallowed. This runs while a 429 is already on its way to the caller, so letting
            // an audit failure escape would turn a request the service correctly refused into a 500 it did
            // not mean to send.
            logger.LogError(exception, "Failed to record rate-limit event {EventId}", rateLimitEvent.EventId);
        }
    }

    /// <summary>
    /// Who the cap applies to. The endpoint is already a route template rather than a path, so a caller
    /// cannot widen their own allowance by varying a route value.
    /// </summary>
    private static string ThrottleKey(RateLimitEvent rateLimitEvent)
    {
        return $"{rateLimitEvent.IpAddress}_{rateLimitEvent.Endpoint}";
    }

    private static string Serialise(RateLimitEvent rateLimitEvent, int suppressed)
    {
        return JsonSerializer.Serialize(new
        {
            eventId = rateLimitEvent.EventId,
            endpoint = rateLimitEvent.Endpoint,
            policy = rateLimitEvent.Policy.ToString(),
            limit = rateLimitEvent.Limit,
            windowSeconds = rateLimitEvent.WindowSeconds,
            ip = rateLimitEvent.IpAddress,
            device = rateLimitEvent.DeviceLabel,
            rejectionsInWindow = rateLimitEvent.RejectionsInWindow,
            suppressedSincePrevious = suppressed,
        });
    }

    private void EmitTelemetry(RateLimitEvent rateLimitEvent)
    {
        telemetryClient.TrackEventIfEnabled(
            TelemetryEventName,
            new Dictionary<string, string>
            {
                ["endpoint"] = rateLimitEvent.Endpoint,
                ["policy"] = rateLimitEvent.Policy.ToString(),
                ["ip"] = rateLimitEvent.IpAddress,
                ["limit"] = rateLimitEvent.Limit.ToString(),
                ["windowSeconds"] = rateLimitEvent.WindowSeconds.ToString(),
                ["rejectionsInWindow"] = rateLimitEvent.RejectionsInWindow.ToString(),
                ["eventId"] = rateLimitEvent.EventId.ToString(),
            });
    }

    private void EmitLog(RateLimitEvent rateLimitEvent)
    {
        // A fixed template so the message text is queryable, and nothing the caller controls is interpolated:
        // the endpoint is a route template, the address is re-rendered by IpAddressNormaliser, and the device
        // is a DeviceLabelResolver label rather than the raw User-Agent.
        logger.LogWarning(
            "Rate limit exceeded: {Policy} at {Endpoint} from {IpAddress} ({DeviceLabel}); limit {Limit} per {WindowSeconds}s, {RejectionsInWindow} rejected this window; eventId {EventId}",
            rateLimitEvent.Policy,
            rateLimitEvent.Endpoint,
            rateLimitEvent.IpAddress,
            rateLimitEvent.DeviceLabel,
            rateLimitEvent.Limit,
            rateLimitEvent.WindowSeconds,
            rateLimitEvent.RejectionsInWindow,
            rateLimitEvent.EventId);
    }
}