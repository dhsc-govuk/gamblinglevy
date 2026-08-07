using Api.Common;
using Application.Common.Security;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Tests.Common;
using Xunit;

namespace Api.Tests.Integration.Security;

/// <summary>
/// BLUM-7239 AC4 — the guard that keeps <see cref="RateLimitPolicyResolver"/>'s table honest against the
/// endpoints that actually exist.
///
/// RateLimitPolicyResolverTests is a hand-written table of route templates copied by a person, so it proves
/// the rules behave as written and nothing about whether they cover the service. This class walks the real
/// <see cref="EndpointDataSource"/> the application builds, which is the only thing that can fail when
/// somebody adds a controller action.
///
/// It is written because the resolver's own class comment claimed it existed. It did not: the reasoning for
/// preferring a resolver over [EnableRateLimiting] attributes was that "a guard test can enumerate
/// EndpointDataSource ... so a new action cannot land unprotected in silence", and the comment asserted a
/// safety net nobody had built — the worst kind of documentation, because a reviewer reads it and stops
/// looking.
/// </summary>
public class RateLimitCoverageTests(IntegrationTestWebFactory factory) : IClassFixture<IntegrationTestWebFactory>
{
    /// <summary>
    /// The only endpoints allowed to be uncounted: Azure's container probes, which are called on a schedule
    /// the service does not control. Anything else appearing here is a hole.
    /// </summary>
    /// <remarks>
    /// The empty string is the root probe: its template is "/", which trimming the leading slash — as the
    /// resolver itself does — reduces to "".
    /// </remarks>
    private static readonly string[] ExpectedExempt = [string.Empty, "health", "uptime"];

    private readonly EndpointDataSource endpoints = factory.Services.GetRequiredService<EndpointDataSource>();

    [Fact]
    public void EveryRegisteredEndpointShouldDrawOnABudget()
    {
        var uncounted = this.RoutedEndpoints()
            .Where(route => RateLimitPolicyResolver.Resolve(route.Method, route.Template) == RateLimitPolicy.None)
            // Minimal-API templates carry a leading slash ("/health") and controller ones do not, so the
            // comparison has to normalise exactly as the resolver does.
            .Where(route => !ExpectedExempt.Contains(route.Template.TrimStart('/'), StringComparer.OrdinalIgnoreCase))
            .Select(route => $"{route.Method} {route.Template}")
            .ToList();

        uncounted.Should().BeEmpty("an endpoint that resolves to None is unlimited, and nothing else reports that");
    }

    [Fact]
    public void EveryExpensiveEndpointShouldDrawOnTheExportBudget()
    {
        // The rule most likely to rot. Two of the three exports are GETs, so a table keyed on the verb would
        // put a ZIP of per-section CSVs across every organisation on the 300-a-minute Read budget.
        var misclassified = this.RoutedEndpoints()
            .Where(route => route.Template.Contains("export", StringComparison.OrdinalIgnoreCase)
                || route.Template.Contains("send-reminder", StringComparison.OrdinalIgnoreCase)
                || (route.Template.Contains("bulk-", StringComparison.OrdinalIgnoreCase)
                    && !route.Template.EndsWith("/template", StringComparison.OrdinalIgnoreCase)))
            .Where(route => RateLimitPolicyResolver.Resolve(route.Method, route.Template) != RateLimitPolicy.Export)
            .Select(route => $"{route.Method} {route.Template}")
            .ToList();

        misclassified.Should().BeEmpty();
    }

    [Fact]
    public void EveryStateChangingEndpointShouldDrawOnAWriteOrStricterBudget()
    {
        var tooLoose = this.RoutedEndpoints()
            .Where(route => !HttpMethods.IsGet(route.Method) && !HttpMethods.IsHead(route.Method) && !HttpMethods.IsOptions(route.Method))
            .Where(route => RateLimitPolicyResolver.Resolve(route.Method, route.Template) is RateLimitPolicy.Read or RateLimitPolicy.None)
            .Select(route => $"{route.Method} {route.Template}")
            .ToList();

        tooLoose.Should().BeEmpty("a request that changes state must never sit on the read budget");
    }

    [Fact]
    public void ShouldHaveFoundTheRealEndpointTable()
    {
        // Without this the three assertions above pass vacuously if endpoint discovery ever returns nothing —
        // which is exactly how a guard test stops guarding without anyone noticing.
        this.RoutedEndpoints().Should().HaveCountGreaterThan(50);
    }

    private List<(string Method, string Template)> RoutedEndpoints()
    {
        return this.endpoints.Endpoints
            .OfType<RouteEndpoint>()
            .SelectMany(
                endpoint => (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"])
                    .Select(method => (Method: method, Template: endpoint.RoutePattern.RawText ?? string.Empty)))
            .Distinct()
            .ToList();
    }
}