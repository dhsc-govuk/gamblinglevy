using System.Security.Claims;
using Domain.ValueObjects;

namespace Api.Middleware;

/// <summary>
/// Blocks every request from a deactivated user, returning an explicit USER_DEACTIVATED envelope the SPA
/// interceptor turns into an "your account has been deactivated" page (BLUM-6386).
///
/// Runs <em>before</em> authorization, unlike the suspension and MFA gates. A deactivated user carries no
/// role claim — ApplicationUserClaimsTransformation withholds it — so UseAuthorization would otherwise
/// answer first with a bodiless 403 and this envelope would never be written. That was the whole bug: the
/// client cannot tell "deactivated" from a broken service when the body is empty.
///
/// Running first also settles precedence deliberately. A deactivated person should not be told to enrol an
/// authenticator, nor that their organisation is suspended — neither is the thing they can act on.
/// /mfa/* is pointedly not exempt: VerifyMfaCommand checks enrolment, lockout and the code but never the
/// user's status, so without this a deactivated user holding a live access token could mint a fresh
/// session.
/// </summary>
public class UserDeactivatedMiddleware
{
    private static readonly HashSet<string> ExemptPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/swagger",
        "/health",

        // Signing out must always be possible, for the reason recorded on OrganisationSuspendedMiddleware:
        // once the SPA is on the page this envelope sends it to, the interceptor turns any further
        // USER_DEACTIVATED response into a promise that never settles — so an unexempt /mfa/logout would
        // leave the page's "Sign out" button hanging for good (BLUM-7242).
        //
        // This exemption only works because ApplyClaims still emits the userId claim to a deactivated
        // principal: MfaController.Logout resolves the caller from it, and answers 401 without it. Removing
        // that claim silently turns this line into decoration.
        "/mfa/logout",
    };

    private readonly RequestDelegate _next;

    public UserDeactivatedMiddleware(RequestDelegate next)
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
        if (GlobalGate.IsExemptPath(path, ExemptPaths))
        {
            await _next(context);
            return;
        }

        var status = context.User.FindFirstValue("status");
        if (string.Equals(status, Status.Deactivated, StringComparison.OrdinalIgnoreCase))
        {
            // Says that the account is deactivated and nothing else — no role, no organisation, no hint at
            // who deactivated it (BLUM-6386 AC4). The SPA states its own copy from the code rather than
            // relaying this message, so keep the two in step by hand.
            await GlobalGate.WriteEnvelopeAsync(
                context,
                StatusCodes.Status403Forbidden,
                "USER_DEACTIVATED",
                "Your account has been deactivated. Contact your administrator.");
            return;
        }

        await _next(context);
    }
}

public static class UserDeactivatedMiddlewareExtensions
{
    public static IApplicationBuilder UseUserDeactivated(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<UserDeactivatedMiddleware>();
    }
}