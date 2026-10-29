using System.Reflection;
using Application.Common.Behaviours;
using Application.Common.Features;
using Application.Common.Permissions;
using Application.DataCollectionForms.Versions;
using Application.MaturityAssessments;
using Application.MaturityAssessments.Prefill;
using Application.Mfa;
using Application.Submissions;
using Application.Submissions.Offline;
using Application.Users;
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
        services.AddSingleton<IFeatureFlags, FeatureFlags>();
        services.AddScoped<PermissionsService>();
        services.AddScoped<MfaAttemptGate>();
        services.AddScoped<MfaFailureRecorder>();
        services.AddScoped<UserDeactivator>();
        services.AddScoped<UserInvitations>();
        services.AddScoped<UserTargetResolver>();
        services.AddScoped<LastActiveAdminGuard>();
        services.AddScoped<SubmissionReviewService>();
        services.AddScoped<QuarterlyReportWriteGate>();
        services.AddScoped<CollectionFormModules>();
        services.AddScoped<PublishedQuestionRules>();
        services.AddScoped<FundingSpendContextResolver>();
        services.AddScoped<StrategicFeedbackContextResolver>();
        services.AddScoped<ActivityReportContextResolver>();
        services.AddScoped<OfflineSheetLoader>();
        services.AddScoped<ActivityResultCarryForward>();
        services.AddScoped<OutstandingSubmissionResolver>();
        services.AddScoped<MaturityAssessmentContextResolver>();
        services.AddScoped<IMaturityPrefillService, MaturityPrefillService>();
        services.AddScoped<MaturityPrefillRefresher>();
    }
}