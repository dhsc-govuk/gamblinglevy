using Application.Common.Seeding;
using Application.Common.Settings;
using Infrastructure.Persistence;

namespace Api.Modules;

internal static class DbModule
{
    extension(WebApplication app)
    {
        internal async Task InitializeDb()
        {
            using var scope = app.Services.CreateScope();
            var initializer = scope.ServiceProvider.GetRequiredService<ApplicationDbContextInitialiser>();
            await initializer.InitialiseAsync();
        }

        // A failed seeding step no longer stops the app (BLUM-8749). Every other step has still run, and a live
        // service on the reference data it already holds is better than one crash-looping over a single drifted
        // row. Each failure was already logged with its exception where it happened, so this summary names the
        // steps and carries no exception; logging it again would report every failure twice. A failed migration
        // still stops startup, because then the schema is not what the code expects.
        internal async Task SeedData()
        {
            using var scope = app.Services.CreateScope();
            var initializer = scope.ServiceProvider.GetRequiredService<ApplicationDbContextInitialiser>();
            try
            {
                await initializer.SeedDataAsync();
            }
            catch (SeedingIncompleteException ex) when (!scope.ServiceProvider.GetRequiredService<ApplicationSettings>().Seeding.FailStartupOnError)
            {
                app.Logger.LogCritical(
                    "Seeding did not complete ({FailedSteps}); starting with the reference data the database already holds.",
                    string.Join(", ", ex.FailedSteps));
            }
        }
    }
}