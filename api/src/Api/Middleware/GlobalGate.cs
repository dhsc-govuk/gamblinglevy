using System.Text.Json;

namespace Api.Middleware;

/// <summary>
/// The two things all three global gates — UserDeactivated, OrganisationSuspended, MfaRequired — did
/// identically in three separate copies. Shared so a change lands once: the exempt-path rule below was
/// wrong in the same way in all three, and a fix applied to one copy would have left the other two
/// exempting routes nobody meant to exempt, with no compiler or test signal.
/// </summary>
internal static class GlobalGate
{
    /// <summary>
    /// Matches an exempt entry exactly, or as a whole leading path segment — never as a bare string prefix.
    /// A raw StartsWith made the exempt list far wider than it reads: "/health" also exempted a future
    /// /healthcheck-details, "/swagger" exempted /swagger-admin, and "/mfa/logout" exempted an
    /// /mfa/logout-everywhere. CLAUDE.md records the same class of bug for RateLimitPolicyResolver, where an
    /// over-broad exempt entry meant a whole control could be bypassed.
    ///
    /// Entries that already end in "/" (MfaRequiredMiddleware exempts the whole of "/mfa/") keep working:
    /// they are a segment prefix by construction.
    /// </summary>
    public static bool IsExemptPath(string path, IEnumerable<string> exemptPaths)
    {
        foreach (var exemptPath in exemptPaths)
        {
            if (string.Equals(path, exemptPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var segmentPrefix = exemptPath.EndsWith('/') ? exemptPath : exemptPath + "/";
            if (path.StartsWith(segmentPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The single place a gate shapes an error body. The `error` code is a contract with the SPA's axios
    /// interceptor, which dispatches on `response.data.error`, so every envelope has to keep the same shape.
    /// </summary>
    public static async Task WriteEnvelopeAsync(HttpContext context, int statusCode, string error, string message)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";

        var response = new
        {
            error,
            message,
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(response));
    }
}