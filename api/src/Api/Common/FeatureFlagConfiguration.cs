namespace Api.Common;

/// <summary>
/// Checks the raw <c>Features</c> configuration section for values the settings binder will not read as a bool.
///
/// The binder does not fail on one: it silently drops the entry, so <c>Features__X=yes</c> leaves the flag off and
/// the key never reaches ApplicationSettings — which also hides it from the unknown-key check. Reading the section
/// directly is the only place the mistake is still visible, so FeatureFlagsModule logs what this returns at boot.
/// </summary>
public static class FeatureFlagConfiguration
{
    public const string SectionName = "Features";

    public static IReadOnlyList<(string Key, string? Value)> InvalidValues(IConfiguration configuration) =>
        configuration.GetSection(SectionName).GetChildren()
            .Where(entry => !bool.TryParse(entry.Value, out _))
            .Select(entry => (entry.Key, entry.Value))
            .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
}