namespace Api.Common;

/// <summary>
/// The browser's own account of where a request came from, read from Sec-Fetch-Site and reduced to a closed
/// set (BLUM-8419).
///
/// A browser sets the header on every request to a secure origin, and it is a forbidden header name, so page
/// script can neither add nor remove it. Anything that is not a browser can send whatever it likes, which is
/// why the raw value is never passed on: the cookie decision and the telemetry both read it through here, so
/// they cannot disagree and nothing caller-chosen reaches a log.
/// </summary>
public static class FetchSite
{
    public const string CrossSite = "cross-site";
    public const string SameSite = "same-site";
    public const string SameOrigin = "same-origin";

    // A request the user started themselves — a typed address or a bookmark. Never an API call from the client.
    public const string None = "none";

    // No header: a browser too old to send it, a proxy that strips it, or a caller that is not a browser.
    public const string Absent = "absent";

    // Present, but not one of the four values the specification defines, or sent more than once.
    public const string Other = "other";

    private const string HeaderName = "Sec-Fetch-Site";

    private static readonly string[] Defined = [CrossSite, SameSite, SameOrigin, None];

    public static string Classify(HttpRequest request)
    {
        var values = request.Headers[HeaderName];
        if (values.Count == 0)
        {
            return Absent;
        }

        if (values.Count > 1)
        {
            return Other;
        }

        return Defined.FirstOrDefault(x => string.Equals(x, values[0], StringComparison.OrdinalIgnoreCase)) ?? Other;
    }

    public static bool IsCrossSite(HttpRequest request) => Classify(request) == CrossSite;
}