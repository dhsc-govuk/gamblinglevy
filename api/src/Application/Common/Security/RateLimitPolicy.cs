using Domain.Users;

namespace Application.Common.Security;

/// <summary>
/// Which budget an endpoint draws on. Chosen by RateLimitPolicyResolver from the HTTP method and the matched
/// route template, rather than by an attribute on each action — one table that can be read in a sitting, and
/// a default that is a line of code somebody chose rather than the silence of a missing attribute.
/// </summary>
public enum RateLimitPolicy
{
    /// <summary>Not counted at all: the container probes, and anything that matched no endpoint.</summary>
    None,

    /// <summary>Safe, cheap reads.</summary>
    Read,

    /// <summary>Anything that changes state.</summary>
    Write,

    /// <summary>Exports and bulk imports — expensive whatever HTTP verb they happen to use.</summary>
    Export,

    /// <summary>The MFA endpoints.</summary>
    Auth,

    /// <summary>The unpartitioned process-wide ceiling.</summary>
    Global,
}

/// <summary>What the limiter decided about one request, and everything needed to describe it in headers.</summary>
/// <param name="Allowed">False when the request must be answered 429.</param>
/// <param name="Policy">The budget that was drawn on — on a rejection, the one that rejected.</param>
/// <param name="Limit">Requests allowed per window, for RateLimit-Limit.</param>
/// <param name="Remaining">Requests left after counting this one, never below zero, for RateLimit-Remaining.</param>
/// <param name="ResetsInSeconds">Seconds until the window closes, never below one, for RateLimit-Reset and Retry-After.</param>
/// <param name="RejectionsInWindow">
/// How many requests this partition has had rejected in the current window, counting this one. One means
/// this is the first, which is what makes a burst of ten thousand collapse to a single audit candidate.
/// </param>
public readonly record struct RateLimitDecision(
    bool Allowed,
    RateLimitPolicy Policy,
    int Limit,
    int Remaining,
    int ResetsInSeconds,
    int RejectionsInWindow);

/// <summary>
/// A rejection, described for the security trail.
///
/// It carries no actor id, and that is not an oversight. The limiter runs before authentication — deliberately,
/// so a flood is shed before it can cost a token validation and a user-profile read — so there is never a
/// user to name. That is the one way this differs from InputRejection, which is built after the pipeline has
/// come back up through authentication and can name one.
/// </summary>
/// <param name="EventId">Identifies the audit row; the target id, since the subject is the request itself.</param>
/// <param name="Endpoint">"POST organisations/local-authorities" — the route template, never the path.</param>
/// <param name="IpAddress">Already through IpAddressNormaliser, so what is stored is never the caller's choice of spelling.</param>
/// <param name="DeviceLabel">A short label, matching what mfa_sessions.device_label holds, never the raw User-Agent.</param>
/// <param name="RejectionsInWindow">The size of the burst this one row stands for.</param>
public record RateLimitEvent(
    Guid EventId,
    string Endpoint,
    RateLimitPolicy Policy,
    string IpAddress,
    string DeviceLabel,
    int Limit,
    int WindowSeconds,
    int RejectionsInWindow)
{
    /// <summary>
    /// Always empty. audit_logs needs an actor and there is not one; UserId.Empty() is the same stand-in
    /// InputRejectionRecorder uses for an unauthenticated rejection.
    /// </summary>
    public static UserId Actor => UserId.Empty();
}