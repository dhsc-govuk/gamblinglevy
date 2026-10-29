using Api.Common;
using Application.Common.Features;
using Application.Common.Settings;

namespace Api.Modules;

internal static class FeatureFlagsModule
{
    extension(WebApplication app)
    {
        // Before the database is touched, so the log says which flags this boot migrated and seeded under. The
        // warnings are the only signal a mistyped Features__ environment variable ever gives: the binder accepts any
        // key, and silently drops a value that is not true/false, so either mistake just leaves the feature off.
        internal void LogFeatureFlags()
        {
            var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("FeatureFlags");
            var enabled = app.Services.GetRequiredService<IFeatureFlags>().Enabled;
            logger.LogInformation(
                "Feature flags enabled: {EnabledFeatures}",
                enabled.Count == 0 ? "none" : string.Join(", ", enabled));

            foreach (var key in FeatureFlags.UnknownKeys(app.Services.GetRequiredService<ApplicationSettings>()))
            {
                logger.LogWarning(
                    "Feature flag setting Features:{FeatureKey} names no known feature and is ignored. Check the spelling, or remove it if the feature has been retired",
                    key);
            }

            foreach (var (key, value) in FeatureFlagConfiguration.InvalidValues(app.Configuration))
            {
                logger.LogWarning(
                    "Feature flag setting Features:{FeatureKey} has the value '{FeatureValue}', which is not true or false, so the flag is off",
                    key,
                    value);
            }
        }
    }
}