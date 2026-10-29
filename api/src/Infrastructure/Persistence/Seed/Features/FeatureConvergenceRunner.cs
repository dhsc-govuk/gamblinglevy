using Application.Common.Features;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Persistence.Seed.Features;

/// <summary>
/// Runs every <see cref="IFeatureConvergence"/> with its flag's current state. It is one seeding step, so a failure is
/// treated like any seeder's (BLUM-8749): it fails the seeding run, but boot logs that and starts anyway. The flag is
/// then on over data that is still in its off state until a later boot converges it. The alternative, a live service
/// crash-looping over one flag's data, is worse.
/// </summary>
public class FeatureConvergenceRunner(
    IEnumerable<IFeatureConvergence> convergences,
    IFeatureFlags featureFlags,
    ILogger<FeatureConvergenceRunner> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var all = convergences.ToList();

        // Two convergences for one flag would both run and could fight over the same rows; one per feature keeps the
        // whole data change for a flag in one place to read and, later, to retire.
        var duplicate = all.GroupBy(convergence => convergence.Feature).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Feature {duplicate.Key} has {duplicate.Count()} data convergences ({string.Join(", ", duplicate.Select(c => c.GetType().Name))}); merge them into one.");
        }

        foreach (var convergence in all)
        {
            var enabled = featureFlags.IsEnabled(convergence.Feature);
            try
            {
                var changed = await convergence.ConvergeAsync(enabled, cancellationToken);
                if (changed > 0)
                {
                    logger.LogInformation(
                        "Feature {Feature} data converged to {FeatureState}: {ChangedRows} row(s) changed.",
                        convergence.Feature,
                        enabled ? "on" : "off",
                        changed);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Converging data for feature {Feature} to {FeatureState} failed.", convergence.Feature, enabled ? "on" : "off");
                throw;
            }
        }
    }
}