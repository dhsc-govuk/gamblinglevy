using Application.Common.Features;

namespace Infrastructure.Persistence.Seed.Features;

/// <summary>
/// Moves a flagged change to existing reference data (labels, required flags, options, routing) into the state its
/// <see cref="Feature"/> calls for. Run by FeatureConvergenceRunner at the end of every SeedDataAsync, so on every
/// boot and every test-data reset. There is no record of what ran last: the data itself is the state, which is what
/// keeps a reset, a restored backup or a half-applied deploy from drifting away from the flag.
///
/// Implementations are picked up from this assembly automatically. Each one must:
/// <list type="bullet">
/// <item>be idempotent in both directions — the same call runs on every boot;</item>
/// <item>guard every UPDATE on the exact value it expects to replace (as the BLUM-8156 wording migration does), so a
/// row a DHSC admin has since edited matches neither state and is left alone;</item>
/// <item>never insert or delete — new rows ship unconditionally and are hidden by the flag-gated read path, because
/// this service is live and saved answers may point at them;</item>
/// <item>leave schema to EF migrations, which are never conditional, and leave rows owned by a seeder that reconciles
/// on every boot to that seeder (it would undo the convergence).</item>
/// </list>
/// </summary>
public interface IFeatureConvergence
{
    Feature Feature { get; }

    /// <summary>Applies the change when <paramref name="enabled"/>, reverts it otherwise.</summary>
    /// <returns>The number of rows changed, which is 0 once converged.</returns>
    Task<int> ConvergeAsync(bool enabled, CancellationToken cancellationToken);
}