using System.Globalization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Modules.Errors;

/// <summary>
/// A 429 that carries its own Retry-After header (BLUM-7239).
///
/// The error factories are pure exception-to-result functions with no HttpContext, so they cannot reach the
/// response to set a header — and pushing the header out to the controller would mean repeating the
/// exception-type test across all seven MfaController actions, which is exactly the duplication the factory
/// pattern exists to avoid.
///
/// A result that writes its own header keeps the decision in one place. It derives from
/// <see cref="ObjectResult"/>, so <c>ToObjectResult()</c> keeps its signature and no call site changes.
/// </summary>
public sealed class RateLimitedObjectResult : ObjectResult
{
    private readonly int retryAfterSeconds;

    public RateLimitedObjectResult(object? value, int retryAfterSeconds)
        : base(value)
    {
        this.StatusCode = StatusCodes.Status429TooManyRequests;

        // Never zero. A Retry-After of 0 tells a conforming client to come straight back, which turns a
        // control meant to slow an attacker down into a busy loop.
        this.retryAfterSeconds = retryAfterSeconds < 1 ? 1 : retryAfterSeconds;
    }

    public override Task ExecuteResultAsync(ActionContext context)
    {
        // Delta-seconds rather than an HTTP-date (RFC 9110 §10.2.3): it does not depend on the client's clock
        // agreeing with ours, and the absolute form is already in the body as lockedUntil for anyone who
        // wants it.
        context.HttpContext.Response.Headers.RetryAfter =
            this.retryAfterSeconds.ToString(CultureInfo.InvariantCulture);

        return base.ExecuteResultAsync(context);
    }
}