using Application.Common.Devices;
using Application.Common.Security;

namespace Api.Common;

/// <summary>
/// Turns the request being limited into a partition key and, if it is refused, a
/// <see cref="RateLimitEvent"/>.
///
/// It lives in the Api layer for the same reason <see cref="InputRejectionContext"/> does: the Application
/// layer has no access to HttpContext, which is also why MFA verification takes its IP and user agent as
/// explicit command parameters rather than reading them ambiently.
/// </summary>
public static class RateLimitContext
{
    /// <summary>
    /// The bucket a request is counted in: policy, method, route template, and normalised caller address.
    ///
    /// Two parts of that are load-bearing and were the mistakes worth not repeating.
    ///
    /// The route <em>template</em>, never the path — <see cref="InputRejectionContext.RouteTemplate"/> is
    /// reused rather than reimplemented, because a path carries route values and
    /// `PUT /activities/{a fresh GUID}` would otherwise mint a bucket per request, so the caller both slips
    /// past their own limit and grows the cache without bound.
    ///
    /// And the address goes through IpAddressNormaliser, never the raw header. X-Azure-ClientIP and
    /// X-Forwarded-For are whatever the caller says they are, so "203.0.113.1", " 203.0.113.1 ",
    /// "203.0.113.1:443" and "[::ffff:203.0.113.1]" would otherwise be four budgets for one caller. It does
    /// not stop them claiming a genuinely different address each time — nothing in this repo can; see the
    /// note on RateLimitingSettings.
    /// </summary>
    public static string PartitionKey(HttpContext context, RateLimitPolicy policy)
    {
        return $"rl_{policy}_{context.Request.Method}_{RouteTemplate(context)}_{ClientIp(context)}";
    }

    /// <summary>The single, unpartitioned key for the process-wide ceiling.</summary>
    public static string GlobalPartitionKey()
    {
        return "rl_global";
    }

    public static RateLimitEvent BuildEvent(HttpContext context, RateLimitDecision decision, int windowSeconds)
    {
        return new RateLimitEvent(
            EventId: Guid.NewGuid(),
            Endpoint: $"{context.Request.Method} {RouteTemplate(context)}",
            Policy: decision.Policy,
            IpAddress: ClientIp(context),
            // The User-Agent is reduced to a short label rather than stored verbatim, matching what
            // mfa_sessions.device_label holds and keeping caller-chosen text out of audit_logs.
            DeviceLabel: DeviceLabelResolver.Resolve(context.Request.Headers.UserAgent.ToString()),
            Limit: decision.Limit,
            WindowSeconds: windowSeconds,
            RejectionsInWindow: decision.RejectionsInWindow);
    }

    public static string RouteTemplate(HttpContext context)
    {
        return InputRejectionContext.RouteTemplate(context);
    }

    private static string ClientIp(HttpContext context)
    {
        return IpAddressNormaliser.Normalise(ClientIpAddress.Resolve(context.Request));
    }
}