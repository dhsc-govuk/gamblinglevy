using Api.Middleware;
using Api.Modules;
using Application;
using Application.Common.Interfaces;
using Infrastructure;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.IdentityModel.Logging;

// A JSON request body here is a form. The largest legitimate one is a report page's answers, each capped at
// 10,000 characters by SubmissionAnswerValidator, so 4 MB leaves ample headroom. The limit used to be
// 1000 MB, which Kestrel will buffer before any handler gets to decide it does not want the request —
// a handful of concurrent large POSTs behind one authenticated session was a memory-pressure DoS
// (BLUM-7240).
const int maxRequestBodySizeMb = 4;

// The two bulk-import actions raise their own ceiling with [RequestSizeLimit(20_000_000)]. That attribute is
// a resource filter applied before model binding and can raise as well as lower, so it overrides the Kestrel
// limit rather than being clamped by it — but the multipart limit is not per-request, so it has to allow
// those uploads through.
const int maxMultipartBodySizeMb = 20;

var builder = WebApplication.CreateBuilder(args);
builder.Services.SetupServices(builder.Configuration);
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);
builder.Services.AddApplicationInsightsTelemetry();

// AddApplicationInsightsTelemetry() registers its own default LoggerFilterRule for the AI provider, floored
// at Warning — added after appsettings.json's "Logging:ApplicationInsights:LogLevel" is already bound, and
// .NET's filter resolution picks the last-registered rule among equally-specific ones, so that config-based
// override alone never takes effect. Re-asserting just the EF command category here, after the SDK call, is
// what actually wins the tie — scoped narrowly rather than globally, since EF's per-query Information-level
// logging at every category would be expensive to ingest at production volume. Added directly to
// LoggerFilterOptions.Rules (rather than the ILoggingBuilder.AddFilter helpers) because the provider is
// matched by its full type name as a string — ApplicationInsightsLoggerProvider itself is not a publicly
// accessible type in this package version — and no AddFilter overload takes both a provider name and a
// category as strings.
builder.Services.Configure<LoggerFilterOptions>(options =>
    options.Rules.Add(new LoggerFilterRule(
        "Microsoft.ApplicationInsights.AspNetCore.Logging.ApplicationInsightsLoggerProvider",
        "Microsoft.EntityFrameworkCore.Database.Command",
        LogLevel.Information,
        filter: null)));

// Sentry predates App Insights becoming this platform's monitoring solution; running both reports every
// exception to two places. Azure App Service injects the connection string as the flat
// APPLICATIONINSIGHTS_CONNECTION_STRING env var (not nested, so it never binds to the
// "ApplicationInsights:ConnectionString" appsettings key on its own) — check both so this is accurate on a
// real deployment, not just against appsettings.json. Sentry stays live wherever App Insights is not
// configured (dev/test, or before the resource is provisioned), so error visibility never silently disappears.
var applicationInsightsConfigured =
    !string.IsNullOrEmpty(builder.Configuration["ApplicationInsights:ConnectionString"]) ||
    !string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]);
if (!applicationInsightsConfigured)
{
    builder.WebHost.UseSentry();
}

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = maxRequestBodySizeMb * 1024 * 1024;
});
builder.Services.Configure<FormOptions>(x =>
{
    x.MultipartBodyLengthLimit = maxMultipartBodySizeMb * 1024 * 1024;
});

var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    IdentityModelEventSource.ShowPII = true;
}

// Data Protection resolves its key ring (locates/creates the key store, loads or generates keys) lazily
// on the first Protect/Unprotect call, not at startup. Without this, that first call is a real user's
// first /mfa/verify on a cold instance — if the key store isn't ready yet, it fails as an unhandled 500
// instead of a startup failure ops can actually see.
using (var dataProtectionWarmupScope = app.Services.CreateScope())
{
    dataProtectionWarmupScope.ServiceProvider.GetRequiredService<IMfaEncryptionService>().Encrypt("startup-warmup");
}

app.LogFeatureFlags();
await app.InitializeDb();
await app.SeedData();
app.UseCors();
app.UseRouting();

// After routing so the matched route template exists to bucket on, and before authentication so a flood is
// shed before it costs a token validation and a user-profile read (BLUM-7239).
app.UseRateLimiting();

// After the limiter, so an unreleased endpoint is still counted, and before authentication, so it answers the
// same 404 to every caller (see FeatureGateMiddleware).
app.UseFeatureGates();
app.UseValidationExceptionHandler();
app.UseAuthentication();

// Before authorization, unlike the two gates below it. A deactivated user carries no role claim, so
// UseAuthorization would answer first with a bodiless 403 and the USER_DEACTIVATED envelope would never
// be written — which is the state BLUM-6386 was raised against. Running first is also the right
// precedence: deactivation is not something an MFA prompt or a suspension notice can be acted on around.
app.UseUserDeactivated();
app.UseAuthorization();
app.UseOrganisationSuspended();
app.UseMfaRequired();

// Last before MVC, so only a caller every gate has admitted gets its body buffered and decoded (BLUM-8917).
app.UseEncodedJsonBodies();
app.MapControllers();
app.MapApiEndpoints();

app.Run();

namespace Api
{
    public partial class Program { }
}