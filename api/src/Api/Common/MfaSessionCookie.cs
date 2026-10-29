using Domain.Users;

namespace Api.Common;

/// <summary>
/// The single definition of the mfa_session cookie — its name and its flags.
///
/// It used to be spelled out privately in MfaController and again in MfaRequiredMiddleware, and BLUM-7241
/// added a third reader in ProfileController. Three copies of "HttpOnly, Secure, SameSite=None" is three
/// chances for one of them to drift, and a cookie written with different flags than it is deleted with is
/// not deleted at all — the browser keeps sending a session the server believes it has just ended.
///
/// The cookie has two shapes (BLUM-8419):
///
/// - Unpartitioned — HttpOnly, Secure, SameSite=None. Always issued. Where the API is on the client's own
///   site it is a first-party cookie and works in every browser.
/// - Partitioned — the same, plus Partitioned (CHIPS). Issued as well, with the same value, to a cross-site
///   request. Our Azure environments serve the client and the API from two *.azurewebsites.net hosts, and
///   that suffix is on the Public Suffix List, so the cookie there is a third-party one; a browser that
///   blocks those (Chrome Incognito and Edge InPrivate by default) discarded it, the code was accepted, and
///   the next request arrived with no session. A partitioned cookie is still stored in that mode.
///
/// The two are different cookies to a browser, which keeps whichever it accepts and sends both if it has
/// both — so they are always written together with one value, and deleting always names both.
///
/// Every line is written with Append, never ResponseCookies.Delete. Delete first strips every mfa_session
/// line already on the response, so a second deletion removes the first and a deletion after the real cookie
/// removes the real cookie. An expired cookie appended is the same header without that side effect.
/// </summary>
public static class MfaSessionCookie
{
    public const string Name = "mfa_session";

    // Not a property on CookieOptions in .NET 10; an extension is written out verbatim after the known flags.
    private const string PartitionedAttribute = "Partitioned";

    /// <summary>
    /// The session named by the request's cookie, or null when there is no cookie or it is not a usable id.
    /// A caller that gets null has learned only that this request carries no session — never that the user
    /// has none.
    ///
    /// A browser holding both shapes sends the name twice. They normally carry the same value; where they do
    /// not, ASP.NET keeps the last one. MfaSessionCookieTests pins that.
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

    /// <summary>
    /// Issues the cookie, and to a cross-site request a partitioned copy of it.
    ///
    /// A copy rather than a replacement, because no one shape works everywhere a cross-site request can come
    /// from: a browser blocking third-party cookies keeps only the partitioned one, and Safari 18.5 to 26.1
    /// is reported to drop a cookie carrying the attribute — so a Safari user who has allowed cross-site
    /// cookies keeps only the unpartitioned one, as they did before. Writing the unpartitioned cookie every
    /// time also overwrites one left by a build that predates the partitioned shape.
    ///
    /// Not partitioned at all unless the browser says the request is cross-site: where the cookie is
    /// first-party it already works, and the same Safari report says the attribute is dropped there too. A
    /// request with no Sec-Fetch-Site gets what it got before.
    /// </summary>
    public static void Set(HttpResponse response, Guid sessionId, DateTime expiresAt)
    {
        var value = sessionId.ToString();

        response.Cookies.Append(Name, value, Options(partitioned: false, expiresAt));

        if (FetchSite.IsCrossSite(response.HttpContext.Request))
        {
            response.Cookies.Append(Name, value, Options(partitioned: true, expiresAt));
        }
    }

    /// <summary>
    /// Deletes the cookie, in both shapes. The flags must match the ones it was written with, minus Expires,
    /// or the browser treats this as a different cookie and leaves the original in place — and which shapes
    /// this browser holds was decided by the request that issued them, not by the one ending the session.
    /// </summary>
    public static void Clear(HttpResponse response)
    {
        response.Cookies.Append(Name, string.Empty, Options(partitioned: false, DateTimeOffset.UnixEpoch));
        response.Cookies.Append(Name, string.Empty, Options(partitioned: true, DateTimeOffset.UnixEpoch));
    }

    // The one place the flags are written, so the cookie issued and the cookie deleted cannot disagree.
    private static CookieOptions Options(bool partitioned, DateTimeOffset expires)
    {
        var options = new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.None,
            Expires = expires,
        };

        if (partitioned)
        {
            options.Extensions.Add(PartitionedAttribute);
        }

        return options;
    }
}