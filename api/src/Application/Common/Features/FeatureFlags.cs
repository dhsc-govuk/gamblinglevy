using Application.Common.Settings;

namespace Application.Common.Features;

/// <summary>
/// <see cref="IFeatureFlags"/> over <see cref="ApplicationSettings.Features"/>.
///
/// Reads the settings on every call rather than copying them at construction: the settings object is the one
/// mutable singleton the integration tests flip per class, and a snapshot would make a flag impossible to switch
/// in a test. Names are matched case-insensitively against the enum's declared names only — not through
/// Enum.TryParse, which would also accept "0" or "1" as a member.
/// </summary>
public class FeatureFlags(ApplicationSettings settings) : IFeatureFlags
{
    private static readonly Feature[] AllFeatures = Enum.GetValues<Feature>();

    private static readonly HashSet<string> KnownNames = new(Enum.GetNames<Feature>(), StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<Feature> Enabled => AllFeatures.Where(IsEnabled).ToList();

    /// <summary>
    /// Configuration keys under <c>Features</c> that name no <see cref="Feature"/> — a typo, or a retired flag
    /// whose environment variable is still set. Logged at boot so the mistake is visible instead of silently
    /// leaving a feature off.
    /// </summary>
    public static IReadOnlyList<string> UnknownKeys(ApplicationSettings settings) =>
        settings.Features.Keys
            .Where(key => !KnownNames.Contains(key))
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public bool IsEnabled(Feature feature)
    {
        var name = feature.ToString();
        if (settings.Features.TryGetValue(name, out var enabled))
        {
            return enabled;
        }

        // The dictionary is declared case-insensitive, but whatever replaces it — the binder, or a test assigning a
        // plain Dictionary — may not keep that comparer, so a differently-cased key falls back to a scan.
        return settings.Features.Any(entry => entry.Value && string.Equals(entry.Key, name, StringComparison.OrdinalIgnoreCase));
    }
}