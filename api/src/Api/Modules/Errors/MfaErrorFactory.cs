using Application.Mfa.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace Api.Modules.Errors;

public static class MfaErrorFactory
{
    public static ObjectResult ToObjectResult(this MfaException exception)
    {
        return exception switch
        {
            MfaRequiredException => new ObjectResult(new { error = "MFA_REQUIRED", message = exception.Message })
            {
                StatusCode = StatusCodes.Status403Forbidden,
            },
            MfaSetupRequiredException => new ObjectResult(new { error = "MFA_SETUP_REQUIRED", message = exception.Message })
            {
                StatusCode = StatusCodes.Status403Forbidden,
            },
            MfaNotEnabledException => new ObjectResult(new { error = "MFA_NOT_ENABLED", message = exception.Message })
            {
                StatusCode = StatusCodes.Status400BadRequest,
            },
            MfaAlreadyEnabledException => new ConflictObjectResult(new { error = "MFA_ALREADY_ENABLED", message = exception.Message }),
            InvalidMfaCodeException => new ObjectResult(new { error = "INVALID_MFA_CODE", message = exception.Message })
            {
                StatusCode = StatusCodes.Status401Unauthorized,
            },
            InvalidRecoveryCodeException => new ObjectResult(new { error = "INVALID_RECOVERY_CODE", message = exception.Message })
            {
                StatusCode = StatusCodes.Status401Unauthorized,
            },
            // Both 429s state their own user-facing copy rather than passing exception.Message through. The
            // exception messages interpolate the user's id, and this body is returned to the caller — so
            // relaying them would answer an unauthenticated-ish guessing endpoint with a real user GUID.
            //
            // They are also two distinct codes on purpose. A client that could not tell the short damper
            // from the lockout would tell somebody locked out for half an hour to "wait a few minutes".
            // retryAfterSeconds is repeated in the body deliberately. Retry-After is not a CORS-safelisted
            // response header, so a browser on another origin can only read it because SetupCors names it in
            // Access-Control-Expose-Headers — and a body field costs nothing, cannot be stripped by a proxy
            // that rewrites headers, and matches the RATE_LIMITED envelope the middleware writes.
            MfaLockedOutException lockedOut => new RateLimitedObjectResult(
                new
                {
                    error = "MFA_LOCKED_OUT",
                    message = "Too many incorrect codes. Your access is locked. Try again later, or tell your administrator.",
                    lockedUntil = lockedOut.LockedUntil,
                    retryAfterSeconds = lockedOut.RetryAfterSeconds,
                },
                lockedOut.RetryAfterSeconds),
            MfaRateLimitException rateLimitEx => new RateLimitedObjectResult(
                new
                {
                    error = "MFA_RATE_LIMITED",
                    message = "Too many attempts. Wait a moment and try again.",
                    lockedUntil = rateLimitEx.LockedUntil,
                    retryAfterSeconds = rateLimitEx.RetryAfterSeconds,
                },
                rateLimitEx.RetryAfterSeconds),
            MfaSetupExpiredException => new ObjectResult(new { error = "MFA_SETUP_EXPIRED", message = exception.Message })
            {
                StatusCode = StatusCodes.Status400BadRequest,
            },
            MfaSetupPendingException => new ObjectResult(new { error = "MFA_SETUP_PENDING", message = exception.Message })
            {
                StatusCode = StatusCodes.Status400BadRequest,
            },
            MfaNotSetupException => new ObjectResult(new { error = "MFA_NOT_SETUP", message = exception.Message })
            {
                StatusCode = StatusCodes.Status400BadRequest,
            },
            MfaSessionNotFoundException => new ObjectResult(new { error = "MFA_SESSION_NOT_FOUND", message = exception.Message })
            {
                StatusCode = StatusCodes.Status404NotFound,
            },
            MfaUnknownException => new ObjectResult(new { error = "MFA_ERROR", message = exception.Message })
            {
                StatusCode = StatusCodes.Status500InternalServerError,
            },
            _ => new ObjectResult(new { error = "MFA_ERROR", message = exception.Message })
            {
                StatusCode = StatusCodes.Status500InternalServerError,
            },
        };
    }
}