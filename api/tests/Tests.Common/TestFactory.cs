using Api;
using Application.Common.Interfaces;
using Application.Common.Settings;
using Application.Users.Interfaces;
using Infrastructure.Blob;
using Infrastructure.Email;
using Infrastructure.Graph;
using Infrastructure.Identity;
using Infrastructure.Mfa;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Testcontainers.PostgreSql;
using Tests.Common.Services;
using Xunit;

namespace Tests.Common;

public class IntegrationTestWebFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder("postgres:15.1-alpine")
        .WithDatabase("gambling-levy-db")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            RegisterDatabase(services);
            RegisterUserServices(services);
            RegisterTestAuthentication(services);
            RemoveBackgroundServices(services);
            OverrideApplicationSettings(services);
        }).ConfigureAppConfiguration((_, config) =>
        {
            config
                .AddJsonFile("appsettings.Test.json")
                .AddEnvironmentVariables();
        });
    }

    /// <summary>
    /// Unhooks MfaSessionRetentionHostedService, which sweeps as soon as the host starts and then on a timer.
    /// Left in, it would be deleting rows out from under any test that seeds a back-dated session — which is
    /// how most of the session tests set up their fixtures — and the failures would be intermittent and land
    /// on whichever test happened to be running. MfaSessionRetentionTests drives IMfaSessionRetentionSweeper
    /// directly instead, so the sweep is still covered, deterministically and without waiting on a clock.
    /// </summary>
    private static void RemoveBackgroundServices(IServiceCollection services)
    {
        var hostedServices = services
            .Where(s => s.ServiceType == typeof(IHostedService)
                        && s.ImplementationType == typeof(MfaSessionRetentionHostedService))
            .ToList();

        foreach (var descriptor in hostedServices)
        {
            services.Remove(descriptor);
        }
    }

    private static void OverrideApplicationSettings(IServiceCollection services)
    {
        var existingDescriptor = services.SingleOrDefault(s => s.ServiceType == typeof(ApplicationSettings));
        var existing = existingDescriptor?.ImplementationInstance as ApplicationSettings;
        if (existing != null)
        {
            existing.Mfa.BypassForTesting = true;

            // Unlimited, because the audit throttle counts per actor and route in IMemoryCache, and that
            // cache outlives an individual test — every test class shares one host. Left at the production
            // caps, the first few security rejections in a class would be audited and the rest suppressed,
            // so whether an assertion passed would depend on how many earlier tests happened to send hostile
            // input (BLUM-7240).
            //
            // Set here rather than in appsettings.Test.json: the JSON block did not bind in time to reach
            // this instance, so it read as configuration while having no effect. The throttle's own logic is
            // covered directly by Application.Tests.Unit/Security/InputRejectionThrottleTests, which is
            // where it belongs — switching a control off globally is not a reason to leave it untested.
            existing.InputValidation.MaxAuditRowsPerActorPerHour = 0;
            existing.InputValidation.MaxAuditRowsPerHour = 0;

            // Off for the same reason, one level up: the request limiter counts per address and route in a
            // cache that outlives an individual test, and the suite drives essentially every endpoint through
            // one HttpClient from one apparent address. Left on, a long test class would start answering 429
            // partway through and which assertion broke would depend on how many requests ran before it.
            //
            // RateLimitingTests turns it back on for its own host — IntegrationTestWebFactory is an
            // IClassFixture, so that instance belongs to that class alone and cannot leak into a parallel one
            // — and clears the counters between methods. The counting itself is covered directly by
            // Application.Tests.Unit/Security/FixedWindowCounterTests (BLUM-7239).
            existing.RateLimiting.Enabled = false;
        }
    }

    // Registered here rather than through WithWebHostBuilder in BaseIntegrationTest's constructor. That
    // constructor runs once per test, and each WithWebHostBuilder call mints a new WebApplicationFactory
    // whose host runs the real entry point — so the app was booting, re-running MigrateAsync and all seven
    // seeders once per test (~360 times a suite) instead of once per test class (~22).
    private static void RegisterTestAuthentication(IServiceCollection services)
    {
        services.AddAuthentication(defaultScheme: "TestScheme")
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("TestScheme", _ => { });
    }

    private void RegisterUserServices(IServiceCollection services)
    {
        services.RemoveServiceByType(typeof(IIdentityService));
        services.AddScoped<IIdentityService, InMemoryIdentityService>();

        services.RemoveServiceByType(typeof(IUsersService));
        services.AddScoped<IUsersService, InMemoryUsersService>();

        services.RemoveServiceByType(typeof(IBlobService));
        services.AddScoped<IBlobService, InMemoryBlobService>();

        services.RemoveServiceByType(typeof(IInvitationEmailService));
        services.AddScoped<IInvitationEmailService, InMemoryInvitationEmailService>();

        services.RemoveServiceByType(typeof(ISubmissionConfirmationEmailService));
        services.AddScoped<ISubmissionConfirmationEmailService, InMemorySubmissionConfirmationEmailService>();

        services.RemoveServiceByType(typeof(INewSignInEmailService));
        services.AddScoped<INewSignInEmailService, InMemoryNewSignInEmailService>();
    }

    private void RegisterDatabase(IServiceCollection services)
    {
        services.RemoveServiceByType(typeof(DbContextOptions<ApplicationDbContext>));

        var dataSourceBuilder = new NpgsqlDataSourceBuilder(_dbContainer.GetConnectionString());
        dataSourceBuilder.EnableDynamicJson();
        var dataSource = dataSourceBuilder.Build();

        services.AddDbContext<ApplicationDbContext>(
            options => options
                .UseNpgsql(
                    dataSource,
                    b => b.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName))
                .UseSnakeCaseNamingConvention()
                .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)));
    }

    public Task InitializeAsync()
    {
        return _dbContainer.StartAsync();
    }

    public new Task DisposeAsync()
    {
        return _dbContainer.DisposeAsync().AsTask();
    }
}

public static class TestFactoryExtensions
{
    public static void RemoveServiceByType(this IServiceCollection services, Type serviceType)
    {
        var descriptor = services.SingleOrDefault(s => s.ServiceType == serviceType);
        if (descriptor is not null)
        {
            services.Remove(descriptor);
        }
    }
}