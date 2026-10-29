namespace Application.Common.Features;

/// <summary>
/// Reads the per-environment feature flags (<see cref="Feature"/>). Always the question to ask — never read
/// <c>ApplicationSettings.Features</c> directly, which is keyed by free-text configuration names.
/// </summary>
public interface IFeatureFlags
{
    /// <summary>Gets the flags switched on in this environment, in declaration order.</summary>
    IReadOnlyList<Feature> Enabled { get; }

    bool IsEnabled(Feature feature);
}