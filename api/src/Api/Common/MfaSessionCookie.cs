using Domain.Users;

namespace Api.Common;

/// <summary>
/// The single definition of the mfa_session cookie — its name and its flags.
///
/// It used to be spelled out privately in MfaController and again in MfaRequiredMiddleware, and BLUM-7241
/// added a third reader in ProfileController. Three copies of "HttpOnly, Secure, SameSite=None" is three
/// chances for one of them to drift, and a cookie written with different flags than it is deleted with is
/// not deleted at all — the browser keeps sending a session the server believes it has just ended.
/// </summary>
public static class MfaSessionCookie
{
    public const string Name = "mfa_session";

    /// <summary>
    /// The session named by the request's cookie, or null when there is no cookie or it is not a usable id.
    /// A caller that gets null has learned only that this request carries no session — never that the user
    /// has none.
    /// </summary>
    public static MfaSessionId? TryRead(HttpRequest request)
    {
        if (!request.Cookies.TryGetValue(Name, out var sessionIdString) ||
            !Guid.TryParse(sessionIdString, out var sessionIdGuid))
        {
            return null;
        }

        return new MfaSessionId(sessionIdGuid);
    }

    public static void Set(HttpResponse response, Guid sessionId, DateTime expiresAt)
    {
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.None,
            Expires = expiresAt,
        };
        response.Cookies.Append(Name, sessionId.ToString(), cookieOptions);
    }

    /// <summary>
    /// Deletes the cookie. The flags must match the ones it was written with, minus Expires, or the browser
    /// treats this as a different cookie and leaves the original in place.
    /// </summary>
    public static void Clear(HttpResponse response)
    {
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.None,
        };
        response.Cookies.Delete(Name, cookieOptions);
    }
}