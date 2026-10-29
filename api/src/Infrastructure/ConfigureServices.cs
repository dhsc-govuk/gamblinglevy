using Application.Common.Interfaces;
using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.Common.Security;
using Application.Common.Settings;
using Infrastructure.AuditLogs;
using Infrastructure.Blob;
using Infrastructure.Caching;
using Infrastructure.Collections;
using Infrastructure.DateTimeProvider;
using Infrastructure.Email;
using Infrastructure.Graph;
using Infrastructure.Identity;
using Infrastructure.Mfa;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Infrastructure.Security;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure;

public static class ConfigureServices
{
    public static void AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<InMemoryCache>();
        services.AddSingleton<IInMemoryCache>(provider => provider.GetRequiredService<InMemoryCache>());
        services.AddSingleton<IUserClaimsCache, UserClaimsCache>();
        services.AddEmailServices();
        services.AddScoped<AuditLogRepository>();
        services.AddScoped<IAuditLogRepository>(provider => provider.GetRequiredService<AuditLogRepository>());
        services.AddScoped<IAuditLogQueries>(provider => provider.GetRequiredService<AuditLogRepository>());
        services.AddScoped<IUserMfaRepository, UserMfaRepository>();
        services.AddScoped<MfaSessionRepository>();
        services.AddScoped<IMfaSessionRepository>(provider => provider.GetRequiredService<MfaSessionRepository>());
        services.AddScoped<IMfaSessionQueries>(provider => provider.GetRequiredService<MfaSessionRepository>());
        services.AddScoped<ITotpService, TotpService>();
        services.AddScoped<IRecoveryCodeService, RecoveryCodeService>();
        services.AddScoped<ITemporaryPasswordGenerator, TemporaryPasswordGenerator>();
        services.AddScoped<IMfaEncryptionService, MfaEncryptionService>();
        services.AddScoped<IMfaSessionService, MfaSessionService>();
        services.AddScoped<IMfaDeviceMismatchRecorder, MfaDeviceMismatchRecorder>();
        services.AddScoped<IMfaSessionRetentionSweeper, MfaSessionRetentionSweeper>();
        services.AddHostedService<MfaSessionRetentionHostedService>();
        services.AddScoped<IAuditLogRetentionSweeper, AuditLogRetentionSweeper>();
        services.AddHostedService<AuditLogRetentionHostedService>();
        services.AddScoped<ICollectionClosureNotifier, CollectionClosureNotifier>();
        services.AddHostedService<CollectionClosureNotificationHostedService>();
        services.AddScoped<ICollectionReminderNotifier, CollectionReminderNotifier>();
        services.AddHostedService<CollectionReminderNotificationHostedService>();
        services.AddScoped<ICollectionWindowClosedLaNotifier, CollectionWindowClosedLaNotifier>();
        services.AddHostedService<CollectionWindowClosedLaNotificationHostedService>();
        services.AddSingleton<IMfaRateLimitService, MfaRateLimitService>();
        // Singleton: the throttle's counters are process-wide, and a scoped instance would gate nothing.
        services.AddSingleton<InputRejectionThrottle>();
        services.AddScoped<IInputRejectionRecorder, InputRejectionRecorder>();

        // Same reasoning for the rate limiter (BLUM-7239): its counters and its private bounded cache are
        // process-wide state, so a scoped instance would count each request against a fresh, empty budget.
        // Registered twice so the integration suite can resolve the concrete type and call Clear() between
        // test methods — one host is shared by a whole test class, so the counters would otherwise carry.
        //
        // The audit throttle gets a bounded cache of its own rather than the shared IMemoryCache. Once the
        // global cap is reached it keeps a suppressed tally per actor, and under a flood spread across many
        // claimed addresses that is one new key per address — in a shared cache with no size limit, sitting
        // alongside the user-claims entries, that is the amplification the limiter exists to prevent.
        services.AddSingleton(provider => new AuditFloodThrottle(
            new MemoryCache(new MemoryCacheOptions
            {
                SizeLimit = provider.GetRequiredService<ApplicationSettings>().RateLimiting.MaxTrackedPartitions,
            }),
            new FixedWindowCounter(provider.GetRequiredService<IDateTimeProvider>())));
        services.AddSingleton<MemoryCacheRateLimiter>();
        services.AddSingleton<IRateLimiter>(provider => provider.GetRequiredService<MemoryCacheRateLimiter>());
        services.AddScoped<IRateLimitEventRecorder, RateLimitEventRecorder>();
        services.AddPersistenceServices(configuration);
        services.AddGraphServices();
        services.AddScoped<IIdentityService, IdentityService>();
        services.AddSingleton<SystemDateTimeProvider>();
        services.AddSingleton<IDateTimeProvider>(provider => provider.GetRequiredService<SystemDateTimeProvider>());
        services.AddBlobServices();
    }
}