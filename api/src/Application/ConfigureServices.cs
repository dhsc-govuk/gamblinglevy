using System.Reflection;
using Application.Common.Behaviours;
using Application.Common.Permissions;
using Application.Mfa;
using Application.Submissions;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Application;

public static class ConfigureServices
{
    public static void AddApplicationServices(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly()));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(UnhandledExceptionBehaviour<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehaviour<,>));
        services.AddScoped<PermissionsService>();
        services.AddScoped<MfaAttemptGate>();
        services.AddScoped<MfaFailureRecorder>();
        services.AddScoped<SubmissionReviewService>();
        services.AddScoped<FundingSpendContextResolver>();
        services.AddScoped<StrategicFeedbackContextResolver>();
        services.AddScoped<ActivityReportContextResolver>();
    }
}