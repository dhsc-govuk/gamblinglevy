using System.Security.Cryptography;
using System.Text;
using Api.Modules.Errors;
using Application.Common.Interfaces;
using Application.Common.Settings;
using Application.DataReset.Commands.ClearMfaData;
using Application.DataReset.Commands.ResetTestData;
using Application.DataReset.Queries;
using Application.UserEmailNormalisation.Commands.NormaliseUserEmails;
using Domain.ValueObjects;
using MediatR;
using Microsoft.AspNetCore.Authorization;

namespace Api.Modules;

internal static class ApiEndpointsModule
{
    private const string DataResetSecretHeader = "X-Data-Reset-Secret";

    internal static WebApplication MapApiEndpoints(this WebApplication app)
    {
        var dateTimeProvider = app.Services.GetRequiredService<IDateTimeProvider>();
        var startupTime = dateTimeProvider.GetUtcNow();
        app.MapGet("/", () => string.Empty);
        app.MapGet("/health", () => "healthy");
        // Role.Admin, not the literal "admin": this is the second routing surface, and a hand-written role string
        // here is exactly the drift the constants exist to prevent (it cannot be found by the same search, and it
        // would survive a rename of the role that the [Authorize] attributes on every controller would not).
        app.MapGet("/uptime", [Authorize(Roles = Role.Admin)] () =>
        {
            var current = dateTimeProvider.GetUtcNow();
            var up = current - startupTime;
            return up.ToString();
        });

        // Test-only full database reset (truncate every table, then reseed), gated by
        // ApplicationSettings.DataReset rather than [Authorize] — see DataResetSettings for why. No user
        // ever holds the shared secret and nothing in the SPA calls this.
        app.MapPost("/internal/test-data/reset", async (
            HttpRequest request,
            ISender sender,
            ApplicationSettings settings,
            CancellationToken cancellationToken) =>
        {
            if (!settings.DataReset.Enabled || !IsValidSecret(request, settings.DataReset.Secret))
            {
                // Same response whether the feature is disabled or the secret is wrong, so a probe cannot
                // tell "not enabled here" apart from "wrong secret".
                return Results.NotFound();
            }

            var result = await sender.Send(new ResetTestDataCommand(), cancellationToken);
            return result.Match<IResult>(
                _ => Results.NoContent(),
                error =>
                {
                    var objectResult = error.ToObjectResult();
                    return Results.Problem(detail: error.Message, statusCode: objectResult.StatusCode);
                });
        });

        // One-time MFA Data-Protection-key cutover (see ApplicationSettings.Mfa.UseBlobKeyPersistence): every
        // already-enrolled user's EncryptedSecret becomes undecryptable the moment key persistence is turned
        // on, so this deletes every user_mfas/mfa_sessions row to force a clean re-enrolment rather than
        // leaving each account to discover its own broken secret one MFA_ERROR at a time. Not routine use —
        // gated the same as the reset above, and same reasoning: no user ever holds the shared secret and
        // nothing in the SPA calls this.
        app.MapPost("/internal/mfa-data/clear", async (
            HttpRequest request,
            ISender sender,
            ApplicationSettings settings,
            CancellationToken cancellationToken) =>
        {
            if (!settings.DataReset.Enabled || !IsValidSecret(request, settings.DataReset.Secret))
            {
                return Results.NotFound();
            }

            var result = await sender.Send(new ClearMfaDataCommand(), cancellationToken);
            return result.Match<IResult>(
                _ => Results.NoContent(),
                error =>
                {
                    var objectResult = error.ToObjectResult();
                    return Results.Problem(detail: error.Message, statusCode: objectResult.StatusCode);
                });
        });

        // One-off backfill for BLUM-7869: normalising email casing at write time doesn't retroactively fix a
        // user (or their Entra account) created before that fix shipped, and editing such a user didn't
        // correct their Entra identity either until this same change fixed UsersService.Update. Walks every
        // user once, correcting Entra first and only then the local row — see NormaliseUserEmailsCommand —
        // and reports any pair of existing accounts that already collide once normalised rather than
        // guessing which one is right. Same gate as the two endpoints above: no user ever holds the shared
        // secret. Unlike those two, this one is meant to be run against Production — an operator enables
        // DataReset only for the run, then disables it again. dryRun defaults true so a bare call previews
        // rather than mutates.
        app.MapPost("/internal/user-emails/normalise", async (
            HttpRequest request,
            ISender sender,
            ApplicationSettings settings,
            CancellationToken cancellationToken,
            bool dryRun = true) =>
        {
            if (!settings.DataReset.Enabled || !IsValidSecret(request, settings.DataReset.Secret))
            {
                return Results.NotFound();
            }

            var result = await sender.Send(new NormaliseUserEmailsCommand(dryRun), cancellationToken);
            return result.Match<IResult>(
                summary => Results.Ok(summary),
                error =>
                {
                    var objectResult = error.ToObjectResult();
                    return Results.Problem(detail: error.Message, statusCode: objectResult.StatusCode);
                });
        });

        // Read-only headline counts for whoever holds the shared secret — most useful right after running
        // internal/mfa-data/clear above, to see how many accounts still need to re-enrol. Same gate as the
        // three endpoints above; unlike those, this has no failure mode worth modelling, so the query returns
        // UserStatistics directly rather than an Either.
        app.MapGet("/internal/user-statistics", async (
            HttpRequest request,
            ISender sender,
            ApplicationSettings settings,
            CancellationToken cancellationToken) =>
        {
            if (!settings.DataReset.Enabled || !IsValidSecret(request, settings.DataReset.Secret))
            {
                return Results.NotFound();
            }

            var statistics = await sender.Send(new GetUserStatisticsQuery(), cancellationToken);
            return Results.Ok(statistics);
        });

        return app;
    }

    private static bool IsValidSecret(HttpRequest request, string? configuredSecret)
    {
        if (string.IsNullOrEmpty(configuredSecret) ||
            !request.Headers.TryGetValue(DataResetSecretHeader, out var provided) ||
            string.IsNullOrEmpty(provided))
        {
            return false;
        }

        var providedBytes = Encoding.UTF8.GetBytes(provided.ToString());
        var configuredBytes = Encoding.UTF8.GetBytes(configuredSecret);

        return providedBytes.Length == configuredBytes.Length &&
               CryptographicOperations.FixedTimeEquals(providedBytes, configuredBytes);
    }
}