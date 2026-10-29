using System.Security.Claims;
using System.Text.Json.Serialization;
using Api.Common;
using Api.Services.Abstract;
using Api.Services.Implementation;
using Application.Common.Interfaces;
using Application.Common.Settings;
using Domain;
using Domain.Users;
using Domain.ValueObjects;
using LanguageExt;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;

namespace Api.Modules;

public class ApplicationUserClaimsTransformation(
    IInMemoryCache cache,
    IUserProfileRepository userProfileRepository)
    : IClaimsTransformation
{
    private async Task<ClaimsPrincipal> GetUserProfile(ClaimsPrincipal principal, SubId subId)
    {
        var cacheKey = $"user::{subId.Value}";
        var cachedUser = cache.GetItem<UserProfile>(cacheKey);

        cachedUser.IfSome(u => ApplyClaims(principal, u));
        if (cachedUser.IsSome)
        {
            return principal;
        }

        var user = await userProfileRepository.GetUserProfile(subId);
        return user.Match(
            u => UpdateCache(u, cacheKey, principal),
            () => principal);
    }

    private ClaimsPrincipal UpdateCache(UserProfile profile, string cacheKey, ClaimsPrincipal principal)
    {
        cache.PutItem(cacheKey, profile, TimeSpan.FromMinutes(5));
        ApplyClaims(principal, profile);

        return principal;
    }

    /// <summary>
    /// The single place claims are decided, so the cache-hit and cache-miss paths cannot disagree about a
    /// deactivated user. They used to: the miss path withheld claims but still cached the profile, and the
    /// hit path then handed that same profile to <see cref="SetUserClaims"/> unconditionally — so the first
    /// request after deactivation was blocked and every request for the next five minutes carried full
    /// role / userId / organisationId claims, repeating on each TTL (BLUM-6386).
    ///
    /// A deactivated user gets exactly two claims: status, and userId. Withholding the *role* claim is what
    /// keeps the original fail-closed property from BLUM-6014 — every [Authorize(Roles = ...)] rejects them
    /// even if UserDeactivatedMiddleware were removed — while the status claim is what lets that middleware
    /// answer with an explicit USER_DEACTIVATED envelope instead of a bodiless 403.
    ///
    /// userId is emitted for the sake of the one endpoint they are still allowed to reach. /mfa/logout is
    /// exempt from the middleware so the deactivated page's Sign out button works, but MfaController.Logout
    /// resolves the caller through PermissionsService.GetUserId() → the userId claim, so withholding it made
    /// the exemption pointless: the endpoint answered 401 "User id is missing", the mfa_sessions row was
    /// never destroyed and every sign-out reported an exception to Sentry. It grants nothing on its own —
    /// the middleware refuses every non-exempt path before authorization ever runs.
    /// </summary>
    private static void ApplyClaims(ClaimsPrincipal principal, UserProfile profile)
    {
        if (profile.Status is not null && profile.Status.Equals(UserStatus.Deactivated))
        {
            SetDeactivatedClaims(principal, profile);
            return;
        }

        SetUserClaims(principal, profile);
    }

    private static void SetDeactivatedClaims(ClaimsPrincipal principal, UserProfile profile)
    {
        ClaimsIdentity claimsIdentity = new();

        if (!principal.HasClaim(claim => claim.Type == "status") && profile.Status is not null)
        {
            claimsIdentity.AddClaim(new Claim("status", profile.Status.ToString()));
        }

        if (!principal.HasClaim(claim => claim.Type == "userId") && profile.Id is not null)
        {
            claimsIdentity.AddClaim(new Claim("userId", profile.Id.ToString()!));
        }

        AddIfAnyClaims(principal, claimsIdentity);
    }

    // Both claim branches are written to be idempotent — every claim is guarded on not already being there —
    // so a second pass over the same principal must be a no-op. Without this it appends an empty identity
    // instead, which is how the deactivated principal's "exactly two claims and nothing else" shape ends up
    // depending on the transformation running exactly once per request rather than on the guards.
    private static void AddIfAnyClaims(ClaimsPrincipal principal, ClaimsIdentity claimsIdentity)
    {
        if (claimsIdentity.Claims.Any())
        {
            principal.AddIdentity(claimsIdentity);
        }
    }

    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        var subIdResult = GetSubId(principal);
        return subIdResult.MatchAsync(
            subId => GetUserProfile(principal, subId),
            () => principal);
    }

    private Option<SubId> GetSubId(ClaimsPrincipal principal)
    {
        var subId = principal.FindFirstValue(ClaimConstants.ObjectId);
        return string.IsNullOrEmpty(subId)
            ? Option<SubId>.None
            : new SubId(new Guid(subId));
    }

    private static void SetUserClaims(ClaimsPrincipal principal, UserProfile profile)
    {
        ClaimsIdentity claimsIdentity = new();
        if (!principal.HasClaim(claim => claim.Type == ClaimConstants.Role))
        {
            claimsIdentity.AddClaim(new Claim(ClaimConstants.Role, profile.Role!.ToString()));
        }

        if (!principal.HasClaim(claim => claim.Type == "userId"))
        {
            var userId = profile.Id;

            claimsIdentity.AddClaim(new Claim("userId", userId!.ToString()));
        }

        if (!principal.HasClaim(claim => claim.Type == "organisationId"))
        {
            var organisationId = profile.OrganisationId.Match(
                orgId => orgId.ToString(),
                () => string.Empty);

            claimsIdentity.AddClaim(new Claim("organisationId", organisationId));
        }

        if (!principal.HasClaim(claim => claim.Type == "locationId"))
        {
            var locationId = profile.LocationId.Match(
                locId => locId.ToString(),
                () => string.Empty);

            claimsIdentity.AddClaim(new Claim("locationId", locationId));
        }

        if (!principal.HasClaim(claim => claim.Type == "organisationStatus"))
        {
            profile.OrganisationStatus.IfSome(orgStatus =>
                claimsIdentity.AddClaim(new Claim("organisationStatus", orgStatus.ToString())));
        }

        if (!principal.HasClaim(claim => claim.Type == "status") && profile.Status is not null)
        {
            claimsIdentity.AddClaim(new Claim("status", profile.Status.ToString()));
        }

        if (!principal.HasClaim(claim => claim.Type == ClaimTypes.Email) && !string.IsNullOrEmpty(profile.Email))
        {
            claimsIdentity.AddClaim(new Claim(ClaimTypes.Email, profile.Email));
        }

        AddIfAnyClaims(principal, claimsIdentity);
    }
}

internal static class SetupModule
{
    extension(IServiceCollection services)
    {
        internal void SetupServices(IConfiguration configuration)
        {
            services.SetupControllers();
            services.AddMemoryCache();
            services.SetupAuthentication(configuration);
            services.SetupCors(configuration);
            services.SetupConfiguration(configuration);
            services.SetupControllerService();
        }

        private void SetupAuthentication(IConfiguration configuration)
        {
            services.AddMicrosoftIdentityWebApiAuthentication(configuration, "AzureAd");
            services.AddAuthorization();
            services.AddTransient<IClaimsTransformation, ApplicationUserClaimsTransformation>();
            services.AddHttpContextAccessor();
        }

        private void SetupCors(IConfiguration configuration)
        {
            var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                ?? Array.Empty<string>();

            services.AddCors(options =>
                options.AddDefaultPolicy(policy =>
                    policy
                        .WithOrigins(allowedOrigins)
                        .AllowAnyHeader()
                        .AllowAnyMethod()
                        // AllowAnyHeader above governs what the browser may *send*. Reading a non-safelisted
                        // response header needs this instead, and none of the rate-limit headers is
                        // safelisted — so without it the SPA sees a 429 with no idea when to come back, while
                        // curl and the integration suite both show the headers present (BLUM-7239 AC5).
                        .WithExposedHeaders(RateLimitHeaders.All)
                        .AllowCredentials()));
        }

        private void SetupControllers()
        {
            services
                .AddControllers()
                .AddJsonOptions(options =>
                {
                    var json = options.JsonSerializerOptions;

                    // Mass assignment (BLUM-7240 AC5). JsonSerializerDefaults.Web leaves this at Skip, so
                    // every request body silently accepted any field a caller cared to invent — the DTO was
                    // documentation, not a contract.
                    json.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;

                    // Type confusion (AC1). The web defaults set AllowReadingFromString, so
                    // {"allocatedAmount":"100"} was accepted as a decimal. Every numeric field the client
                    // sends is already a JSON number — the forms coerce with Number()/parseAmount() at the
                    // mutation boundary — so nothing legitimate depends on the coercion.
                    json.NumberHandling = JsonNumberHandling.Strict;

                    // The deepest legitimate body is a funding forecast: root, activities, an activity,
                    // quarters, a quarter. Five levels, so 16 keeps threefold headroom while stopping a
                    // nesting bomb from spending the whole body budget on stack depth.
                    json.MaxDepth = 16;

                    // Injection characters (AC6), applied at the only place that sees every string in every
                    // body regardless of which DTO it binds to.
                    json.Converters.Add(new SanitisedStringJsonConverter());
                })
                .ConfigureApiBehaviorOptions(options =>
                {
                    // Without this, a binding or parse failure answers in the framework's
                    // ValidationProblemDetails shape instead of the API's own envelope, and leaks .NET type
                    // names, JSON paths and byte offsets while doing it.
                    options.InvalidModelStateResponseFactory = InvalidModelStateResponse.Create;
                });

            services.Configure<MvcOptions>(MvcModelBindingMessages.Configure);
        }

        private void SetupConfiguration(IConfiguration configuration)
        {
            var settings = configuration.Get<ApplicationSettings>();
            if (settings != null)
            {
                services.AddSingleton(settings);
            }
        }

        private void SetupControllerService()
        {
            services.AddScoped<IActivitiesControllerService, ActivitiesControllerService>();
            services.AddScoped<IFundingDashboardControllerService, FundingDashboardControllerService>();
            services.AddScoped<IFormDesignerControllerService, FormDesignerControllerService>();
            services.AddScoped<IDataCollectionsControllerService, DataCollectionsControllerService>();
            services.AddScoped<IDhscSubmissionsControllerService, DhscSubmissionsControllerService>();
            services.AddScoped<ILaSubmissionsControllerService, LaSubmissionsControllerService>();
            services.AddScoped<IDataCollectionFormsControllerService, DataCollectionFormsControllerService>();
            services.AddScoped<IAdminHomeControllerService, AdminHomeControllerService>();
            services.AddScoped<IAdminUsersControllerService, AdminUsersControllerService>();
            services.AddScoped<ILocationsControllerService, LocationsControllerService>();
            services.AddScoped<IOrganisationManagerHomeControllerService, OrganisationManagerHomeControllerService>();
            services.AddScoped<IOrganisationsControllerService, OrganisationsControllerService>();
            services.AddScoped<IOrganisationUsersControllerService, OrganisationUsersControllerService>();
            services.AddScoped<IProfileControllerService, ProfileControllerService>();
            services.AddScoped<IQuarterlyReportControllerService, QuarterlyReportControllerService>();
            services.AddScoped<IQuarterlyReportOfflineControllerService, QuarterlyReportOfflineControllerService>();
            services.AddScoped<IReportSnapshotService, ReportSnapshotService>();
            services.AddScoped<IQuarterlyReportExportService, QuarterlyReportExportService>();
            services.AddScoped<IReportExportSizeService, ReportExportSizeService>();
            services.AddScoped<IActivityRecordExportService, ActivityRecordExportService>();
            services.AddScoped<IMaturityAssessmentControllerService, MaturityAssessmentControllerService>();
            services.AddScoped<IMaturityAssessmentSnapshotService, MaturityAssessmentSnapshotService>();
            services.AddScoped<IMaturityAssessmentExportService, MaturityAssessmentExportService>();
        }
    }
}