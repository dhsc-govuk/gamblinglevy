using System.Security.Claims;
using Application.Common.Devices;
using Application.Common.Security;
using Domain.Users;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Api.Common;

/// <summary>
/// Builds an <see cref="InputRejection"/> from the request being rejected.
///
/// It lives in the Api layer because the Application layer has no access to HttpContext — the same reason
/// MFA verification takes its IP and user agent as explicit command parameters rather than reading them
/// ambiently.
/// </summary>
public static class InputRejectionContext
{
    private static readonly string[] ExemptPaths = ["/", "/health", "/uptime"];

    private const string UnknownRoute = "unknown";

    public static bool IsExempt(HttpContext context)
    {
        return ExemptPaths.Contains(context.Request.Path.Value, StringComparer.OrdinalIgnoreCase);
    }

    public static InputRejection Build(HttpContext context, IEnumerable<InputRejectionReason> reasons)
    {
        return new InputRejection(
            RejectionId: Guid.NewGuid(),
            Endpoint: $"{context.Request.Method} {RouteTemplate(context)}",
            ActorId: ResolveActor(context),
            // Never the raw header. Both X-Azure-ClientIP and X-Forwarded-For are attacker-settable, and
            // this value is written to audit_logs — IpAddressNormaliser re-renders it from a parsed address
            // or falls back to "unknown", so what is stored is never the caller's choice of spelling
            // (the lesson of BLUM-7241).
            IpAddress: IpAddressNormaliser.Normalise(ClientIpAddress.Resolve(context.Request)),
            // Likewise the User-Agent is reduced to a short label rather than stored verbatim, matching
            // what mfa_sessions.device_label holds.
            DeviceLabel: DeviceLabelResolver.Resolve(context.Request.Headers.UserAgent.ToString()),
            Reasons: reasons.ToList());
    }

    /// <summary>
    /// The matched route pattern — "organisations/local-authorities/{organisationId:guid}" — not the request
    /// path.
    ///
    /// Two reasons, both learned the hard way. It is the key InputRejectionThrottle buckets a caller's audit
    /// cap by, and a path carries route values, so a caller could mint a fresh bucket per request and both
    /// evade their own cap and grow an unbounded cache. And it is written verbatim into audit_logs.After, so
    /// a template keeps caller-chosen text out of the audit payload entirely.
    ///
    /// Routing has always run by the time either caller reaches this — model binding happens after endpoint
    /// selection, and the middleware's catch runs on the way back up — so the fallback is for the paths that
    /// match no endpoint at all.
    ///
    /// Internal rather than private because RateLimitContext buckets on the same thing, for the first of
    /// those two reasons. Two definitions of "which bucket is this request in" is exactly the drift this
    /// comment exists to prevent, so the limiter reuses this rather than growing its own copy.
    /// </summary>
    internal static string RouteTemplate(HttpContext context)
    {
        return context.GetEndpoint() is RouteEndpoint endpoint
            ? endpoint.RoutePattern.RawText ?? UnknownRoute
            : UnknownRoute;
    }

    /// <summary>
    /// The userId claim, or null when the request never authenticated.
    ///
    /// Worth knowing why this is populated at all in ValidationExceptionMiddleware, which Program.cs
    /// registers before UseAuthentication: the catch block runs on the way back up, after authentication has
    /// already executed downstream, so HttpContext.User is fully populated by the time it is reached.
    /// </summary>
    private static UserId? ResolveActor(HttpContext context)
    {
        var claim = context.User.FindFirstValue("userId");
        return Guid.TryParse(claim, out var userId) ? new UserId(userId) : null;
    }

    /// <summary>
    /// Works out what kind of thing was wrong with a value that failed to bind or parse.
    ///
    /// This has to read the message, which is not the obvious design. MVC records a deserialisation failure
    /// with ModelStateDictionary.TryAddModelError(key, exception, metadata), and that overload treats an
    /// InputFormatterException as already user-facing: it keeps the message and **discards the exception**.
    /// So by the time ModelState is read there is no exception left to switch on, and the message is the
    /// only thing that survives.
    ///
    /// Only the input formatter produces a System.Text.Json path as the key, so a leading '$' is what
    /// separates a deserialiser message from one written by a validation attribute. Within those, our own
    /// converter is recognised by a constant we own, and an unmapped member by a substring the framework
    /// owns — which is why InputValidationTests pins both, so a wording change in a future .NET surfaces as
    /// a failing test rather than as a silently missing audit row.
    /// </summary>
    public static string KindForModelStateError(string modelStateKey, ModelError error)
    {
        if (error.Exception is UnsafeInputJsonException)
        {
            return InputRejectionClassifier.UnsafeCharacterKind;
        }

        if (!IsDeserialiserKey(modelStateKey))
        {
            return InputRejectionClassifier.FieldRuleKind;
        }

        if (error.ErrorMessage == UnsafeInputJsonException.ErrorMessage)
        {
            return InputRejectionClassifier.UnsafeCharacterKind;
        }

        return error.ErrorMessage.Contains("could not be mapped to any", StringComparison.Ordinal)
            ? InputRejectionClassifier.UnknownFieldKind
            : InputRejectionClassifier.MalformedValueKind;
    }

    public static bool IsDeserialiserKey(string modelStateKey)
    {
        return modelStateKey.StartsWith('$');
    }
}