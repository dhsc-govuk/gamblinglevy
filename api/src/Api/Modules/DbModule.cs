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

        internal async Task SeedData()
        {
            using var scope = app.Services.CreateScope();
            var initializer = scope.ServiceProvider.GetRequiredService<ApplicationDbContextInitialiser>();
            await initializer.SeedDataAsync();
        }
    }
}