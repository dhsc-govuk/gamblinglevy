using Api;
using Application.Common.Interfaces;
using Application.Common.Settings;
using Application.Users.Interfaces;
using Infrastructure.AuditLogs;
using Infrastructure.Blob;
using Infrastructure.Collections;
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
using Tests.Common.Services;
using Xunit;

namespace Tests.Common;

public class IntegrationTestWebFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // This class's own database on the run's shared server (TestPostgres), created before the host boots so that
    // MigrateAsync and every seeder run against it exactly as they did against a dedicated container.
    private TestDatabase? _database;
    private NpgsqlDataSource? _dataSource;

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
    /// Unhooks the background workers, which sweep as soon as the host starts and then on a timer.
    ///
    /// MfaSessionRetentionHostedService would be deleting rows out from under any test that seeds a back-dated
    /// session — which is how most of the session tests set up their fixtures — and the failures would be
    /// intermittent and land on whichever test happened to be running.
    ///
    /// CollectionClosureNotificationHostedService (BLUM-7719) is worse: its startup sweep would stamp
    /// closure_notified_at on, and push email records for, any collection another test class left closed and
    /// unnotified — into a recorder that is static and therefore shared across the whole assembly.
    ///
    /// CollectionReminderNotificationHostedService (BLUM-7571, and BLUM-7721's window-open announcement, which
    /// shares the same sweep) is worse again: its startup sweep would fan emails out for any collection another
    /// test class left open — every assigned organisation for the announcement, not just the outstanding ones —
    /// into the same kind of static recorder, AND write collection_reminder_dispatches rows under that class's
    /// feet, so a later assertion on "was this trigger point claimed?" would fail for reasons nothing in that
    /// test caused.
    ///
    /// AuditLogRetentionHostedService (BLUM-8750) is off while Audit.RetentionDays is 0, but the settings
    /// singleton is shared and AuditLogRetentionTests switches it on, so a sweep could delete back-dated audit
    /// rows that any other test seeds and then asserts on.
    ///
    /// Every sweep is still covered, deterministically and without waiting on a clock, by driving
    /// IMfaSessionRetentionSweeper / ICollectionClosureNotifier / ICollectionReminderNotifier /
    /// IAuditLogRetentionSweeper directly (MfaSessionRetentionTests, CollectionClosureNotificationTests,
    /// CollectionReminderNotificationTests, AuditLogRetentionTests).
    /// Anything added here must be added to the filter below too.
    /// </summary>
    private static void RemoveBackgroundServices(IServiceCollection services)
    {
        var hostedServices = services
            .Where(s => s.ServiceType == typeof(IHostedService)
                        && (s.ImplementationType == typeof(MfaSessionRetentionHostedService)
                            || s.ImplementationType == typeof(AuditLogRetentionHostedService)
                            || s.ImplementationType == typeof(CollectionClosureNotificationHostedService)
                            || s.ImplementationType == typeof(CollectionReminderNotificationHostedService)))
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

            // Same JSON-binding-arrives-too-late reason as everything else here: appsettings.Test.json's
            // DataReset block never reaches this instance, so the suite's reset-endpoint tests would 404
            // unconditionally without setting it directly.
            existing.DataReset.Enabled = true;
            existing.DataReset.Secret = "integration-test-harness-reset-secret";

            // Deployed environments log a failed seeding step and start anyway (BLUM-8749). Here it must stop the
            // host, or a seeding defect would show up only as unrelated assertion failures in whichever tests
            // happen to need the missing rows. SeedingFailureBootTests turns it back off for its own host.
            existing.Seeding.FailStartupOnError = true;

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

        services.RemoveServiceByType(typeof(ICollectionClosedEmailService));
        services.AddScoped<ICollectionClosedEmailService, InMemoryCollectionClosedEmailService>();

        services.RemoveServiceByType(typeof(ICollectionWindowClosedLaEmailService));
        services.AddScoped<ICollectionWindowClosedLaEmailService, InMemoryCollectionWindowClosedLaEmailService>();

        services.RemoveServiceByType(typeof(ITemporaryPasswordEmailService));
        // A singleton, unlike its neighbours: it holds what was sent on the instance so the record is this host's
        // alone (see the class). The interface stays scoped, as production registers it.
        services.AddSingleton<InMemoryTemporaryPasswordEmailService>();
        services.AddScoped<ITemporaryPasswordEmailService>(provider => provider.GetRequiredService<InMemoryTemporaryPasswordEmailService>());

        services.RemoveServiceByType(typeof(ICollectionReminderEmailService));
        services.AddScoped<ICollectionReminderEmailService, InMemoryCollectionReminderEmailService>();

        services.RemoveServiceByType(typeof(ICollectionReminderSummaryEmailService));
        services.AddScoped<ICollectionReminderSummaryEmailService, InMemoryCollectionReminderSummaryEmailService>();

        services.RemoveServiceByType(typeof(ICollectionWindowOpenedEmailService));
        services.AddScoped<ICollectionWindowOpenedEmailService, InMemoryCollectionWindowOpenedEmailService>();

        services.RemoveServiceByType(typeof(ICollectionWindowOpenedSummaryEmailService));
        services.AddScoped<ICollectionWindowOpenedSummaryEmailService, InMemoryCollectionWindowOpenedSummaryEmailService>();

        services.RemoveServiceByType(typeof(IMaturityAssessmentConfirmationEmailService));
        services.AddScoped<IMaturityAssessmentConfirmationEmailService, InMemoryMaturityAssessmentConfirmationEmailService>();
    }

    private void RegisterDatabase(IServiceCollection services)
    {
        services.RemoveServiceByType(typeof(DbContextOptions<ApplicationDbContext>));

        var connectionString = _database?.ConnectionString
            ?? throw new InvalidOperationException("The test database is created in InitializeAsync, before the host is built.");

        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.EnableDynamicJson();
        var dataSource = dataSourceBuilder.Build();
        _dataSource = dataSource;

        services.AddDbContext<ApplicationDbContext>(
            options => options
                .UseNpgsql(
                    dataSource,
                    b => b.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName))
                .UseSnakeCaseNamingConvention()
                .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)));
    }

    public async Task InitializeAsync()
    {
        _database = await TestPostgres.CreateDatabaseAsync();
    }

    // Runs before xUnit's Dispose() stops the host, as disposing the container used to. The host is idle between
    // classes (its background services are removed above), so nothing needs the database after this point.
    public new async Task DisposeAsync()
    {
        if (_dataSource is not null)
        {
            // Closes this class's idle pooled connections now instead of leaving them to hold server connection slots
            // until Npgsql's idle pruning, which would otherwise accumulate across the run on the one shared server.
            await _dataSource.DisposeAsync();
        }

        if (_database is not null)
        {
            await TestPostgres.DropDatabaseAsync(_database);
        }
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