using System.Text.Json;
using Api.Common;

namespace Api.Middleware;

/// <summary>
/// Unwraps a Base64-encoded JSON body on an action marked <see cref="AcceptsEncodedJsonBodyAttribute"/>
/// (BLUM-8917). The client encodes the body so that DAC's gateway WAF, whose OWASP CRS rules match on ordinary
/// answer text and escaped JSON, has nothing to match; this puts the original JSON back before MVC reads it.
///
/// Decoding is not a bypass of anything this API checks. The decoded body goes through model binding exactly as
/// a plain one would, so SanitisedStringJsonConverter, the validators and the input-rejection audit all run on
/// it. Family Hubs found that HTML-entity encoding and a JSON file part in a multipart body were both still
/// inspected by the same kind of WAF; Base64 was the encoding that got through.
///
/// Not feature-flagged: it shipped as a production fix. Plain JSON is still accepted on the same actions, so a tab
/// loaded before the client update keeps working. A request without the header passes through untouched, and so
/// does one to an action without the attribute (its envelope then simply fails model binding as any wrong-shaped
/// body would). Registered after the authentication and authorisation gates, so nothing is buffered for a caller
/// who is about to be refused.
/// </summary>
public class EncodedJsonBodyMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue(AcceptsEncodedJsonBodyAttribute.HeaderName, out var encoding)
            || context.GetEndpoint()?.Metadata.GetMetadata<AcceptsEncodedJsonBodyAttribute>() is null)
        {
            await next(context);
            return;
        }

        var json = string.Equals(encoding, AcceptsEncodedJsonBodyAttribute.HeaderValue, StringComparison.OrdinalIgnoreCase)
            ? await TryDecodeAsync(context)
            : null;

        if (json is null)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "application/json";
            var envelope = ValidationErrorEnvelope.From([new ValidationErrorItem("Body", "The request body could not be read.")]);
            await context.Response.WriteAsync(envelope.ToJson());
            return;
        }

        var body = new MemoryStream(json, writable: false);
        context.Response.RegisterForDispose(body);
        context.Request.Body = body;
        context.Request.ContentLength = json.Length;
        context.Request.ContentType = "application/json; charset=utf-8";
        context.Request.Headers.Remove(AcceptsEncodedJsonBodyAttribute.HeaderName);

        await next(context);
    }

    /// <summary>The decoded UTF-8 JSON, or null when the envelope is not one a conforming client sends.</summary>
    private static async Task<byte[]?> TryDecodeAsync(HttpContext context)
    {
        try
        {
            using var envelope = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
            if (envelope.RootElement.ValueKind != JsonValueKind.Object
                || !envelope.RootElement.TryGetProperty(AcceptsEncodedJsonBodyAttribute.PayloadProperty, out var payload)
                || payload.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            return payload.TryGetBytesFromBase64(out var bytes) ? bytes : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public static class EncodedJsonBodyMiddlewareExtensions
{
    public static IApplicationBuilder UseEncodedJsonBodies(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<EncodedJsonBodyMiddleware>();
    }
}