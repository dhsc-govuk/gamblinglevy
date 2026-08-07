using Application.Common.Security;

namespace Api.Common;

/// <summary>
/// Picks the budget an endpoint draws on, from its HTTP method and matched route template (BLUM-7239 AC4).
///
/// A resolver rather than [EnableRateLimiting] attributes on each action, for three reasons. The attributes
/// only feed the framework's own rate-limiting middleware, which this feature cannot use — it has no hook on
/// a successful request, so it can attach the RateLimit-* headers of AC5 to a 429 and to nothing else. The
/// table is worth being able to read in one sitting rather than reconstructing from a hundred actions across
/// sixteen controllers. And with attributes, an action with no attribute is unlimited and looks exactly like
/// an action nobody thought about; here the default is a line somebody chose, and
/// RateLimitCoverageTests walks the real EndpointDataSource so a new action cannot land unprotected in
/// silence.
///
/// Pure and static so it can be tested as a table, with no host and no HttpContext.
/// </summary>
public static class RateLimitPolicyResolver
{
    /// <summary>
    /// The container probes only. Azure calls / and /health on a schedule the service does not control, so
    /// counting them would spend a budget on the platform's own health checking and take the instance out of
    /// rotation.
    ///
    /// "unknown" — the template <see cref="InputRejectionContext.RouteTemplate"/> returns for a request that
    /// matched no endpoint — is deliberately NOT here, though it was. Exempting it meant every request to a
    /// path that does not exist was uncounted: the middleware returned before charging the caller's partition
    /// <em>and</em> before charging the unpartitioned Global ceiling, so a flood aimed at any nonexistent URL
    /// bypassed the whole control and, worse, bypassed the one counter documented as the answer to a caller
    /// who varies the address they claim. Unmatched requests now fall through to the ordinary method rules
    /// and share one bucket per caller, keyed on the literal template "unknown", which is what bounds a
    /// scanner without giving it a fresh bucket per invented path.
    /// </summary>
    private static readonly string[] ExemptTemplates = ["", "/", "health", "uptime"];

    public static RateLimitPolicy Resolve(string method, string routeTemplate)
    {
        // Controller routes arrive without a leading slash ("activities/{activityId:guid}"), minimal-API
        // ones with ("/health"). Normalising here keeps every rule below written one way.
        var template = (routeTemplate ?? string.Empty).TrimStart('/');

        if (ExemptTemplates.Contains(template, StringComparer.OrdinalIgnoreCase)
            || template.StartsWith("swagger", StringComparison.OrdinalIgnoreCase))
        {
            return RateLimitPolicy.None;
        }

        // Only the MFA endpoints that actually take a credential. A GET under /mfa/ is a status read that the
        // SPA issues on mount, and bucketing it with the code-submission endpoints put routine page loads on
        // the tightest per-address budget in the service — which, behind one council's NAT egress, is a whole
        // organisation sharing an allowance sized for one attacker.
        if (template.StartsWith("mfa", StringComparison.OrdinalIgnoreCase)
            && !HttpMethods.IsGet(method)
            && !HttpMethods.IsHead(method))
        {
            return RateLimitPolicy.Auth;
        }

        // Deliberately above the method rules. Two of the three exports are GETs
        // ("quarterly-report/{collectionId:guid}/export"), and an export builds a ZIP of per-section CSVs
        // across every organisation in the collection — treating it as an ordinary read at three hundred a
        // minute would be the single worst outcome of a table keyed only on the verb.
        if (IsExpensive(method, template))
        {
            return RateLimitPolicy.Export;
        }

        return HttpMethods.IsGet(method) || HttpMethods.IsHead(method)
            ? RateLimitPolicy.Read
            : RateLimitPolicy.Write;
    }

    private static bool IsExpensive(string method, string template)
    {
        if (template.Contains("export", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Fans a SendGrid email out to every reporting organisation in the collection — around a hundred and
        // fifty of them. Outbound cost per request is nothing like an ordinary write's.
        if (template.Contains("send-reminder", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // The two bulk uploads accept 20 MB bodies, raising their own ceiling past Kestrel's 4 MB with
        // [RequestSizeLimit]. Their sibling ".../template" downloads share the prefix but are a few hundred
        // bytes of static CSV, so they stay ordinary reads — otherwise fetching the template twice would eat
        // a fifth of the budget needed to then perform the import.
        return template.Contains("bulk-", StringComparison.OrdinalIgnoreCase)
            && !template.EndsWith("/template", StringComparison.OrdinalIgnoreCase)
            && !HttpMethods.IsGet(method);
    }
}