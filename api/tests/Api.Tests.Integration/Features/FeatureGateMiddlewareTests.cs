using Api.Common;
using Api.Middleware;
using Application.Common.Features;
using Application.Common.Settings;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Api.Tests.Integration.Features;

/// <summary>
/// The 404 an unreleased endpoint answers with while its feature flag is off.
///
/// Lives in this project because the middleware is in the Api layer, which Application.Tests.Unit does not
/// reference. It declares no class fixture, so it costs no PostgreSQL container. The endpoint is built by hand
/// because no real endpoint is gated yet; once one is, a request against it through the real pipeline belongs
/// in that feature's own test class.
/// </summary>
public class FeatureGateMiddlewareTests
{
    private const Feature GatedFeature = Feature.BLUM_7637;

    [Fact]
    public async Task ShouldAnswer404AndStop_WhenTheRequiredFeatureIsOff()
    {
        var (context, nextCalled) = await InvokeAsync(new ApplicationSettings(), new RequiresFeatureAttribute(GatedFeature));

        context.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        nextCalled.Should().BeFalse("an unreleased endpoint must not run at all, not merely hide its response");
    }

    [Fact]
    public async Task ShouldPassThrough_WhenTheRequiredFeatureIsOn()
    {
        var (context, nextCalled) = await InvokeAsync(SettingsWith(GatedFeature), new RequiresFeatureAttribute(GatedFeature));

        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task ShouldPassThrough_WhenTheEndpointIsNotGated()
    {
        var (_, nextCalled) = await InvokeAsync(new ApplicationSettings());

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task ShouldPassThrough_WhenNoEndpointMatched()
    {
        // Routing found nothing; the 404 for that belongs to the framework, and the rate limiter has already
        // counted the request against its "unknown" bucket.
        var (_, nextCalled) = await InvokeAsync(new ApplicationSettings(), endpoint: null);

        nextCalled.Should().BeTrue();
    }

    private static Task<(HttpContext Context, bool NextCalled)> InvokeAsync(ApplicationSettings settings, params object[] metadata) =>
        InvokeAsync(settings, new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(metadata), "gated endpoint"));

    private static async Task<(HttpContext Context, bool NextCalled)> InvokeAsync(ApplicationSettings settings, Endpoint? endpoint)
    {
        var context = new DefaultHttpContext();
        context.SetEndpoint(endpoint);
        var nextCalled = false;

        var middleware = new FeatureGateMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            new FeatureFlags(settings));
        await middleware.InvokeAsync(context);

        return (context, nextCalled);
    }

    private static ApplicationSettings SettingsWith(Feature feature)
    {
        var settings = new ApplicationSettings();
        settings.Features[feature.ToString()] = true;
        return settings;
    }
}