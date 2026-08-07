using Api.Middleware;
using Api.Modules;
using Application;
using Infrastructure;
using Microsoft.AspNetCore.DataProtection;
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
builder.Services.AddDataProtection().SetApplicationName("GamblingLevyPortal");
builder.Services.AddApplicationInsightsTelemetry();
builder.WebHost.UseSentry();

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

await app.InitializeDb();
await app.SeedData();
app.UseCors();
app.UseRouting();

// After routing so the matched route template exists to bucket on, and before authentication so a flood is
// shed before it costs a token validation and a user-profile read (BLUM-7239).
app.UseRateLimiting();
app.UseValidationExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.UseOrganisationSuspended();
app.UseMfaRequired();
app.MapControllers();
app.MapApiEndpoints();
app.UseSentryTracing();

app.Run();

namespace Api
{
    public partial class Program { }
}