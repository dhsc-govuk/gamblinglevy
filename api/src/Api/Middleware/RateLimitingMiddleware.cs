using System.Globalization;
using System.Text.Json;
using Api.Common;
using Application.Common.Security;
using Application.Common.Settings;

namespace Api.Middleware;

/// <summary>
/// Per-address request limiting (BLUM-7239 AC4/AC5/AC6).
///
/// Program.cs registers this after UseRouting and before UseAuthentication, and both halves of that matter.
///
/// After routing, because the bucket is the matched route template and <c>GetEndpoint()</c> is null until
/// routing has run. Before authentication, because authentication is the expensive part of this pipeline —
/// JWT validation followed by ApplicationUserClaimsTransformation, which reads UserProfileRepository whenever
/// the claims cache misses. Shedding a flood after paying for that would be shedding it too late.
///
/// The cost of that ordering, accepted rather than fixed: nothing has authenticated yet, so the partition
/// cannot include the caller's user id. AC4 asks for per-address limits, so this is the right shape, but it
/// does mean one abusive authenticated user and an anonymous scanner sharing an egress address share a
/// budget. The per-user control that this cannot express lives inside the MFA handlers.
///
/// This is a hand-written middleware rather than the framework's UseRateLimiter because of AC5.
/// Microsoft.AspNetCore.RateLimiting exposes OnRejected and nothing else — no hook on a successful request,
/// and no access to the PartitionedRateLimiter it builds internally — so it can attach RateLimit-* headers
/// to a 429 and to nothing else, which is the opposite of what those headers are for. They exist so a
/// well-behaved client can slow down <em>before</em> it is refused.
/// </summary>
public class RateLimitingMiddleware(RequestDelegate next, ILogger<RateLimitingMiddleware> logger)
{
    /// <summary>
    /// The wire contract, matching the { error, message } shape MfaRequiredMiddleware and
    /// OrganisationSuspendedMiddleware already write. The SPA's axios interceptor dispatches on
    /// <c>response.data.error</c>, so this string is a contract with client/src/helpers/authFailure.ts.
    /// </summary>
    private const string ErrorCode = "RATE_LIMITED";

    /// <summary>
    /// ApplicationSettings arrives per request rather than through the constructor, following
    /// MfaRequiredMiddleware. That is not cosmetic: the settings object is a mutable singleton, and reading
    /// it per request is what lets an integration test switch the limiter on for its own host and have the
    /// very next request observe it. Captured in the constructor, the test would be configuring a snapshot.
    /// </summary>
    public async Task InvokeAsync(
        HttpContext context,
        IRateLimiter limiter,
        ApplicationSettings settings)
    {
        var limits = settings.RateLimiting;

        // Costs nothing when off — no counting, no headers, no allocation. This is the switch the whole
        // integration suite runs behind.
        if (!limits.Enabled)
        {
            await next(context);
            return;
        }

        // Preflights are short-circuited inside UseCors before routing, so this should never see one. Belt
        // and braces: a browser must never be told its preflight was rate limited, because it surfaces as an
        // opaque CORS failure rather than as the 429 it is.
        if (HttpMethods.IsOptions(context.Request.Method))
        {
            await next(context);
            return;
        }

        var policy = RateLimitPolicyResolver.Resolve(context.Request.Method, RateLimitContext.RouteTemplate(context));
        if (policy == RateLimitPolicy.None)
        {
            await next(context);
            return;
        }

        var policySettings = For(limits, policy);
        var decision = limiter.Acquire(RateLimitContext.PartitionKey(context, policy), policy, policySettings);

        if (!decision.Allowed)
        {
            await this.RejectAsync(context, decision, policySettings.WindowSeconds);
            return;
        }

        // Only once the partition has admitted the request. Charging the shared ceiling for a request that
        // was already refused would let one caller spend everybody's budget — the same lesson
        // AuditFloodThrottle records about not counting suppressed events.
        var globalSettings = limits.Global;
        var globalDecision = limiter.Acquire(RateLimitContext.GlobalPartitionKey(), RateLimitPolicy.Global, globalSettings);

        if (!globalDecision.Allowed)
        {
            // Retry-After, but no RateLimit-* — the caller still needs to know when to come back, and is told.
            // What they are not told is the number, because the process ceiling is not their budget: it is the
            // service's total capacity, and publishing it states exactly how much traffic is needed to keep
            // everybody else out. The per-partition budgets above are the caller's own and are advertised.
            await this.RejectAsync(context, globalDecision, globalSettings.WindowSeconds, describeBudget: false);
            return;
        }

        // Registered rather than written now, so the headers survive anything downstream that resets the
        // response, and so they are attached exactly once immediately before the first byte. Writing them
        // eagerly would work today only because nothing else in this pipeline calls Response.Clear().
        context.Response.OnStarting(() =>
        {
            WriteLimitHeaders(context, decision);
            return Task.CompletedTask;
        });

        await next(context);
    }

    private static RateLimitPolicySettings For(RateLimitingSettings limits, RateLimitPolicy policy)
    {
        return policy switch
        {
            RateLimitPolicy.Read => limits.Read,
            RateLimitPolicy.Write => limits.Write,
            RateLimitPolicy.Export => limits.Export,
            RateLimitPolicy.Auth => limits.Auth,
            RateLimitPolicy.Global => limits.Global,

            // None never reaches here, and a new policy added without a budget must fail loudly rather than
            // silently becoming unlimited.
            _ => throw new ArgumentOutOfRangeException(nameof(policy), policy, "No rate-limit budget is configured for this policy."),
        };
    }

    private static void WriteLimitHeaders(HttpContext context, RateLimitDecision decision)
    {
        // Limit 0 means the policy is configured unlimited. Advertising "RateLimit-Limit: 0" would tell a
        // conforming client it may never call again, so an unlimited policy simply says nothing.
        if (decision.Limit <= 0)
        {
            return;
        }

        context.Response.Headers[RateLimitHeaders.Limit] = decision.Limit.ToString(CultureInfo.InvariantCulture);
        context.Response.Headers[RateLimitHeaders.Remaining] = decision.Remaining.ToString(CultureInfo.InvariantCulture);

        // Delta-seconds until the window closes, not a timestamp. The draft these fields come from is
        // explicit about that, and it avoids depending on the client's clock agreeing with ours.
        context.Response.Headers[RateLimitHeaders.Reset] = decision.ResetsInSeconds.ToString(CultureInfo.InvariantCulture);
    }

    private async Task RejectAsync(HttpContext context, RateLimitDecision decision, int windowSeconds, bool describeBudget = true)
    {
        await this.RecordAsync(context, decision, windowSeconds);

        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.ContentType = "application/json";

        if (describeBudget)
        {
            WriteLimitHeaders(context, decision);
        }

        // The first Retry-After this API has ever sent. Delta-seconds form (RFC 9110 §10.2.3) rather than an
        // HTTP-date, for the same clock-skew reason as RateLimit-Reset.
        context.Response.Headers[RateLimitHeaders.RetryAfter] = decision.ResetsInSeconds.ToString(CultureInfo.InvariantCulture);

        var response = new
        {
            error = ErrorCode,
            message = $"Too many requests. Try again in {decision.ResetsInSeconds} seconds.",
            retryAfterSeconds = decision.ResetsInSeconds,
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(response));
    }

    /// <summary>
    /// Recorded before the body is written, for the reason ValidationExceptionMiddleware gives: the response
    /// is streamed, so the caller sees the 429 as soon as it flushes whether or not this has finished.
    /// Recording first makes the trail deterministic instead of a race against how fast the caller reads.
    ///
    /// The recorder is resolved from RequestServices rather than injected because this middleware is a
    /// singleton in the pipeline and the recorder depends on the scoped audit repository. Resolution itself
    /// is guarded: it throws rather than returning null when the object graph cannot be built, or when the
    /// scope has already been torn down by a client that abandoned the connection — which is precisely what
    /// a flood looks like.
    /// </summary>
    private async Task RecordAsync(HttpContext context, RateLimitDecision decision, int windowSeconds)
    {
        try
        {
            var recorder = context.RequestServices.GetService<IRateLimitEventRecorder>();
            if (recorder is null)
            {
                return;
            }

            await recorder.RecordAsync(RateLimitContext.BuildEvent(context, decision, windowSeconds), CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to record a rate-limit rejection for {Path}", context.Request.Path);
        }
    }
}

public static class RateLimitingMiddlewareExtensions
{
    public static IApplicationBuilder UseRateLimiting(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<RateLimitingMiddleware>();
    }
}