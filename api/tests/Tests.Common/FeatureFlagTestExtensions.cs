using Application.Common.Features;
using Application.Common.Settings;
using Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Common;

public static class FeatureFlagTestExtensions
{
    /// <summary>
    /// Switches a feature flag on this class's host and re-applies the flag-dependent part of the seed, which is what
    /// the restart that applies a flag in a deployed environment does (the rest of the seed is unaffected by a flag,
    /// so re-running it would only cost time). Every flag is off in the suite by default, matching production.
    ///
    /// The flag lives on the ApplicationSettings singleton, which belongs to this class's host alone
    /// (IntegrationTestWebFactory is an IClassFixture), so a flip cannot leak into a parallel class. It does
    /// outlive the test, though — a flag-sensitive test sets the state it needs on its first line rather than
    /// assuming the default, so the order the class runs in never matters.
    /// </summary>
    public static async Task SetFeatureAsync(this IntegrationTestWebFactory factory, Feature feature, bool enabled)
    {
        factory.Services.GetRequiredService<ApplicationSettings>().Features[feature.ToString()] = enabled;

        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContextInitialiser>().ApplyFeatureFlagsAsync();
    }
}