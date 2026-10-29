using Application.Common.Features;

namespace Api.Common;

/// <summary>
/// Marks a controller, action or minimal endpoint (<c>.WithMetadata(new RequiresFeatureAttribute(...))</c>) as
/// existing only while <see cref="Feature"/> is switched on. FeatureGateMiddleware answers a bare 404 otherwise.
///
/// Gate the endpoint rather than leaving its route unmapped: a mapped route stays visible to
/// AuthorizationCoverageTests and RateLimitCoverageTests, which run with every flag off, so a gated endpoint's
/// [Authorize] and rate-limit policy are still checked before the feature is released. The trade-off is that the
/// route's existence stays observable (see FeatureGateMiddleware); leave it unmapped instead when that matters.
/// Several attributes may be stacked; every named feature must be on.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class RequiresFeatureAttribute(Feature feature) : Attribute
{
    public Feature Feature { get; } = feature;
}