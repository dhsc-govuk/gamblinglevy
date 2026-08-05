using Application.Common.Interfaces;
using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.Common.Security;
using Infrastructure.Blob;
using Infrastructure.Caching;
using Infrastructure.DateTimeProvider;
using Infrastructure.Email;
using Infrastructure.Graph;
using Infrastructure.Identity;
using Infrastructure.Mfa;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Infrastructure.Security;
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
        services.AddScoped<IMfaEncryptionService, MfaEncryptionService>();
        services.AddScoped<IMfaSessionService, MfaSessionService>();
        services.AddScoped<IMfaSessionRetentionSweeper, MfaSessionRetentionSweeper>();
        services.AddHostedService<MfaSessionRetentionHostedService>();
        services.AddSingleton<IMfaRateLimitService, MfaRateLimitService>();
        // Singleton: the throttle's counters are process-wide, and a scoped instance would gate nothing.
        services.AddSingleton<InputRejectionThrottle>();
        services.AddScoped<IInputRejectionRecorder, InputRejectionRecorder>();
        services.AddPersistenceServices(configuration);
        services.AddGraphServices();
        services.AddScoped<IIdentityService, IdentityService>();
        services.AddSingleton<SystemDateTimeProvider>();
        services.AddSingleton<IDateTimeProvider>(provider => provider.GetRequiredService<SystemDateTimeProvider>());
        services.AddBlobServices();
    }
}