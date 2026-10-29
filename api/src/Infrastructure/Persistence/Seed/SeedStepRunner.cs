using Application.Common.Seeding;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Persistence.Seed;

/// <summary>A named seeding step, run by <see cref="SeedStepRunner"/>.</summary>
public sealed record SeedStep(string Name, Func<Task> Run);

/// <summary>
/// Runs the seeding steps in order, and runs every one of them whatever happened to the one before (BLUM-8749).
/// A seeder used to rethrow straight out of startup, so one drifted reference row, or two instances seeding at once,
/// crash-looped a live App Service. Now a failed step is logged and the rest still run. If any step failed, the run
/// ends by throwing <see cref="SeedingIncompleteException"/>. At boot, Api.Modules.DbModule logs that and starts the
/// app on the reference data it has. The test-data reset and the integration suite let it propagate, so a seeding
/// defect still fails them loudly.
/// </summary>
public static class SeedStepRunner
{
    /// <param name="steps">The steps, in the order they must run.</param>
    /// <param name="resetAfterFailure">
    /// Called after each failure, before the next step. The steps share one DbContext, and a failed step's rows are
    /// still tracked as Added, so the next step's SaveChangesAsync would try to write them again.
    /// </param>
    /// <param name="logger">Where each failure is logged as an error.</param>
    public static async Task RunAsync(IReadOnlyList<SeedStep> steps, Action resetAfterFailure, ILogger logger)
    {
        var failures = new List<(string Name, Exception Error)>();

        foreach (var step in steps)
        {
            try
            {
                await step.Run();
            }
            catch (Exception ex)
            {
                resetAfterFailure();
                logger.LogError(ex, "Seeding step {SeedStep} failed; the remaining steps still run.", step.Name);
                failures.Add((step.Name, ex));
            }
        }

        if (failures.Count > 0)
        {
            throw new SeedingIncompleteException(failures.Select(f => f.Name).ToList(), failures.Select(f => f.Error));
        }
    }
}