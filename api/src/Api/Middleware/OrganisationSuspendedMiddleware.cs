using System.Security.Claims;
using System.Text.Json;
using Domain.Organisations;

namespace Api.Middleware;

/// <summary>
/// Blocks every request from a user whose organisation has been suspended, returning an explicit
/// ORGANISATION_SUSPENDED envelope the SPA interceptor turns into a "contact DHSC" page. Runs after
/// authorization and before MFA enforcement so suspension takes priority over an MFA prompt. DHSC
/// admins carry no organisationStatus claim, so they are never blocked.
/// </summary>
public class OrganisationSuspendedMiddleware
{
    private static readonly HashSet<string> ExemptPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/swagger",
        "/health",

        // Signing out must always be possible. Blocking it gains nothing — logout only destroys the
        // caller's own session — and the SPA's interceptor turns the suspension envelope into a promise
        // that never settles, so a suspended user could never complete sign-out at all (BLUM-7242).
        "/mfa/logout",
    };

    private readonly RequestDelegate _next;

    public OrganisationSuspendedMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.User.Identity?.IsAuthenticated ?? true)
        {
            await _next(context);
            return;
        }

        var path = context.Request.Path.Value ?? string.Empty;
        if (IsExemptPath(path))
        {
            await _next(context);
            return;
        }

        var organisationStatus = context.User.FindFirstValue("organisationStatus");
        if (string.Equals(organisationStatus, OnboardingStatuses.Suspended, StringComparison.OrdinalIgnoreCase))
        {
            await WriteOrganisationSuspendedResponse(context);
            return;
        }

        await _next(context);
    }

    private static bool IsExemptPath(string path)
    {
        foreach (var exemptPath in ExemptPaths)
        {
            if (path.StartsWith(exemptPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task WriteOrganisationSuspendedResponse(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/json";

        var response = new
        {
            error = "ORGANISATION_SUSPENDED",
            message = "Your organisation's access has been suspended. Contact DHSC.",
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(response));
    }
}

public static class OrganisationSuspendedMiddlewareExtensions
{
    public static IApplicationBuilder UseOrganisationSuspended(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<OrganisationSuspendedMiddleware>();
    }
}