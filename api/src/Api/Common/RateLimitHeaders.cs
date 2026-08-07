namespace Api.Common;

/// <summary>
/// The response headers the rate limiter sets (BLUM-7239 AC5).
///
/// They live here rather than as private constants on the middleware because setting them is only half the
/// job: none of the four is a CORS-safelisted response header, so a browser on a different origin — which is
/// every deployed environment, the API and the SPA being separate App Services — cannot read them unless the
/// CORS policy names them in Access-Control-Expose-Headers. <see cref="All"/> is what SetupCors exposes, so
/// adding a header in one place without the other is impossible.
///
/// This is worth stating plainly because the failure is silent and looks like success everywhere it is
/// usually checked: the headers are on the wire, so curl shows them, the network tab shows them, and the
/// integration suite — which is not a browser and has no origin — asserts them happily. Only the SPA sees
/// nothing. downloadFile.ts has quietly lived with the same problem for content-disposition.
/// </summary>
public static class RateLimitHeaders
{
    public const string Limit = "RateLimit-Limit";
    public const string Remaining = "RateLimit-Remaining";
    public const string Reset = "RateLimit-Reset";
    public const string RetryAfter = "Retry-After";

    /// <summary>
    /// Not a rate-limit header, and included here anyway because this is the list SetupCors exposes and it is
    /// the only such list the API has. downloadFile.ts reads Content-Disposition to name every export and
    /// template the service produces, and it is no more safelisted than the four above — so until it was
    /// exposed, every download in every deployed environment was silently saved under the client's hard-coded
    /// fallback name instead of the server-generated one that identifies the organisation and period.
    ///
    /// It predates BLUM-7239, but that ticket built the mechanism that fixes it, named this exact case in
    /// three comments, and then exposed only its own headers. Fixing it costs one array entry.
    /// </summary>
    public const string ContentDisposition = "Content-Disposition";

    /// <summary>Everything the CORS policy exposes, so the middleware and SetupCors cannot drift apart.</summary>
    public static readonly string[] All = [Limit, Remaining, Reset, RetryAfter, ContentDisposition];
}