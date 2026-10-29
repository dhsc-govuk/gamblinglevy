using Api.Common;
using Application.Common.Features;

namespace Api.Middleware;

/// <summary>
/// Answers 404, with no body, for an endpoint carrying a <see cref="RequiresFeatureAttribute"/> whose feature is
/// switched off in this environment — the same status as a route that does not exist, which is how this client
/// has always hidden an unreleased surface (the forms builder's unregistered routes).
///
/// It hides the endpoint's behaviour, not the fact that its route is mapped. The gate only runs once routing has
/// matched the gated endpoint, so a request with another verb on the same path still gets routing's 405, and the
/// limiter has already charged the route's own template bucket (with RateLimit-* headers) rather than the shared
/// "unknown" one. That is fine for work that is merely unreleased. A feature whose route names must not be
/// discoverable before release should leave the route unregistered until then instead.
///
/// Registered after UseRateLimiting, so a gated route is still counted — an exemption from the limiter is an
/// exemption from its Global ceiling too, which is the bypass the "unknown" route template once opened. And
/// before authentication, so the answer does not depend on who is asking: an unreleased endpoint is 404 to an
/// anonymous caller and to a DHSC admin alike, and costs no token validation or profile read.
/// </summary>
public class FeatureGateMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IFeatureFlags _featureFlags;

    public FeatureGateMiddleware(RequestDelegate next, IFeatureFlags featureFlags)
    {
        _next = next;
        _featureFlags = featureFlags;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var required = context.GetEndpoint()?.Metadata.GetOrderedMetadata<RequiresFeatureAttribute>();
        if (required?.Any(gate => !_featureFlags.IsEnabled(gate.Feature)) == true)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await _next(context);
    }
}

public static class FeatureGateMiddlewareExtensions
{
    public static IApplicationBuilder UseFeatureGates(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<FeatureGateMiddleware>();
    }
}