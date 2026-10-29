using Api.Common;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Application.Common.Settings;
using Application.Common.Telemetry;
using Domain.Users;
using Microsoft.ApplicationInsights;

namespace Api.Middleware;

public class MfaRequiredMiddleware
{
    // The values mfa_required's "reason" can take (BLUM-8321). Spelled out rather than taken from
    // MfaSessionStatus.ToString() so a query has one naming style to match, and renaming an enum member
    // cannot silently change what a saved query or alert is looking for.
    private const string ReasonNoCookie = "no_cookie";
    private const string ReasonNotFound = "not_found";
    private const string ReasonNotOwned = "not_owned";
    private const string ReasonDeviceMismatch = "device_mismatch";
    private const string ReasonOther = "other";

    private static readonly HashSet<string> ExemptPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/mfa/",
        "/swagger",
        "/health",
    };

    private readonly RequestDelegate _next;

    public MfaRequiredMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IIdentityService identityService,
        IMfaSessionService sessionService,
        IUserMfaRepository userMfaRepository,
        TelemetryClient telemetryClient,
        ApplicationSettings settings)
    {
        if (settings.Mfa.BypassForTesting)
        {
            await _next(context);
            return;
        }

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

        var userIdOption = identityService.GetUserId();
        if (userIdOption.IsNone)
        {
            await _next(context);
            return;
        }

        var userId = userIdOption.Match(id => id, () => throw new InvalidOperationException());
        var userMfaOption = await userMfaRepository.GetByUserIdAsync(userId);

        if (userMfaOption.IsNone)
        {
            await WriteMfaSetupRequiredResponse(context);
            return;
        }

        var userMfa = userMfaOption.Match(mfa => mfa, () => throw new InvalidOperationException());

        // A row with IsEnabled == false means enrolment was started but never completed — POST /mfa/setup
        // creates exactly that. Letting it through would hand anyone who opens the setup page and walks
        // away unrestricted access with no MFA session at all (BLUM-7242). Push them back into setup; the
        // /mfa/* routes stay exempt above, so enrolment can still complete.
        if (!userMfa.IsEnabled)
        {
            await WriteMfaSetupRequiredResponse(context);
            return;
        }

        var sessionId = MfaSessionCookie.TryRead(context.Request);
        if (sessionId is null)
        {
            TrackMfaRequired(telemetryClient, context, userId, ReasonNoCookie);
            await WriteMfaRequiredResponse(context);
            return;
        }

        var ipAddress = GetClientIpAddress(context);
        var userAgent = context.Request.Headers.UserAgent.ToString();

        // The caller's id goes in so the service can refuse — and decline to slide the idle window for —
        // a session that belongs to somebody else.
        var status = await sessionService.ValidateSessionAsync(sessionId, userId, ipAddress, userAgent);

        // A timed-out session is reported separately from a missing one, and as 401 rather than 403: the SPA
        // cannot otherwise tell "you were signed out for your security" from "you never verified", only the
        // former should end the CIAM sign-in rather than prompting for another code, and the SPA's 403
        // handler carries a 5-second post-verification grace window that must not swallow a timeout.
        switch (status)
        {
            case MfaSessionStatus.Expired:
                await WriteEndedSessionResponse(context, "SESSION_EXPIRED", "Your session has reached its maximum length. Sign in again.");
                return;

            case MfaSessionStatus.IdleTimeout:
                await WriteEndedSessionResponse(context, "SESSION_IDLE_TIMEOUT", "You were signed out because of inactivity. Sign in again.");
                return;

            // A revocation is a deliberate act — the user ended this session from their sessions list, or an
            // admin deactivated them, or the concurrency cap evicted it (BLUM-7241). It joins the timeouts on
            // 401 for the same reason: answering MFA_REQUIRED would send this browser back to the code entry
            // screen, where a correct code would mint a fresh session and quietly undo the revocation.
            case MfaSessionStatus.Revoked:
                await WriteEndedSessionResponse(context, "SESSION_REVOKED", "You were signed out because this session was ended.");
                return;

            case MfaSessionStatus.Valid:
                await _next(context);
                return;

            // NotFound, NotOwned and DeviceMismatch: this request is not carrying a session we can act on, so
            // asking for verification is the right answer.
            default:
                TrackMfaRequired(telemetryClient, context, userId, ReasonFor(status));
                await WriteMfaRequiredResponse(context);
                return;
        }
    }

    private static bool IsExemptPath(string path) => GlobalGate.IsExemptPath(path, ExemptPaths);

    // "other" only if a status is ever added that reaches the MFA_REQUIRED branch without being named here;
    // the envelope still goes out either way, so this must not throw.
    private static string ReasonFor(MfaSessionStatus status) => status switch
    {
        MfaSessionStatus.NotFound => ReasonNotFound,
        MfaSessionStatus.NotOwned => ReasonNotOwned,
        MfaSessionStatus.DeviceMismatch => ReasonDeviceMismatch,
        _ => ReasonOther,
    };

    /// <summary>
    /// MFA_REQUIRED stands for four different situations — no cookie, no such session, somebody else's
    /// session, or a device-binding mismatch — and the client sees the same envelope for all of them. The
    /// reason goes to telemetry so that, if users keep being sent back to the code-entry screen and none of
    /// these events says DeviceMismatch, it is still possible to tell which branch is firing (BLUM-8321).
    /// Telemetry only: the no-cookie case is part of every ordinary sign-in, far too common for audit_logs.
    ///
    /// fetchSite is the browser's own Sec-Fetch-Site (BLUM-8419), reduced to FetchSite's closed set so that
    /// nothing a caller chose is written out. "cross-site" means the client and the API are on different
    /// sites in that environment, so mfa_session is a third-party cookie there — which is the only way to
    /// see how an environment we do not deploy is laid out, and whether a no_cookie straight after a
    /// successful verification is a browser refusing to store it.
    /// </summary>
    private static void TrackMfaRequired(TelemetryClient telemetryClient, HttpContext context, UserId userId, string reason) =>
        telemetryClient.TrackEventIfEnabled(
            "mfa_required",
            new Dictionary<string, string>
            {
                ["reason"] = reason,
                ["userId"] = userId.Value.ToString(),
                ["path"] = context.Request.Path.Value ?? string.Empty,
                ["fetchSite"] = FetchSite.Classify(context.Request),
            });

    /// <summary>
    /// The three answers that mean "the session this cookie names is gone for good" — timed out, or revoked.
    /// Each one deletes the cookie on the way out, and that is load-bearing rather than tidiness.
    ///
    /// The client responds to these by ending the CIAM sign-in rather than by re-verifying, so nothing in
    /// that flow ever overwrites the cookie the way /mfa/verify does. Leave it in place and the browser
    /// re-authenticates, immediately presents the same dead cookie, is told the same thing again, and is
    /// signed straight back out — a loop the user cannot break, because the cookie is HttpOnly and the SPA
    /// cannot reach it either. Deleting it means the next request simply carries no session, which is the
    /// ordinary MFA_REQUIRED case that does lead back to /mfa/verify. Safe in every case: the record is
    /// already revoked, so the cookie could never be honoured again anyway.
    /// </summary>
    private static Task WriteEndedSessionResponse(HttpContext context, string error, string message)
    {
        MfaSessionCookie.Clear(context.Response);
        return WriteJsonError(context, StatusCodes.Status401Unauthorized, error, message);
    }

    private static Task WriteMfaRequiredResponse(HttpContext context) =>
        WriteJsonError(context, StatusCodes.Status403Forbidden, "MFA_REQUIRED", "MFA verification required");

    private static Task WriteMfaSetupRequiredResponse(HttpContext context) =>
        WriteJsonError(context, StatusCodes.Status403Forbidden, "MFA_SETUP_REQUIRED", "MFA setup required");

    /// <summary>
    /// The single place this middleware shapes an error body. The `error` code is a contract with the SPA's
    /// axios interceptor, which dispatches on `response.data.error`, so every envelope has to keep the same
    /// shape — one writer is what stops two of three copies drifting and silently ceasing to match.
    /// </summary>
    private static Task WriteJsonError(HttpContext context, int statusCode, string error, string message) =>
        GlobalGate.WriteEnvelopeAsync(context, statusCode, error, message);

    private static string GetClientIpAddress(HttpContext context) => ClientIpAddress.Resolve(context.Request);
}

public static class MfaRequiredMiddlewareExtensions
{
    public static IApplicationBuilder UseMfaRequired(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<MfaRequiredMiddleware>();
    }
}