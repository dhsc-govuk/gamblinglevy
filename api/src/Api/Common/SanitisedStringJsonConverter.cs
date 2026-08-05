using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Common.Text;

namespace Api.Common;

/// <summary>
/// Applies RequestStringNormaliser to every string in every request body.
///
/// Registered globally rather than declared per DTO, and deliberately not written as a FluentValidation
/// rule. A rule protects the fields somebody remembered to write a rule for — across nearly sixty
/// validators, with every new DTO property unprotected the day it is added. That fail-open shape is the
/// CWE-20 pattern this ticket exists to close. A converter is fail-closed by construction: it sees every
/// string in every body, nested and in arrays, with no opt-in and no new call sites.
/// </summary>
public sealed class SanitisedStringJsonConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();

        if (!RequestStringNormaliser.TryNormalise(value, out var normalised))
        {
            // System.Text.Json leaves a message we supplied alone — it only appends its own diagnostics to
            // exceptions it raised itself — but still records the path. MVC's input formatter turns this
            // into a ModelState error, so it lands in InvalidModelStateResponse with everything else, in
            // the one envelope, naming no character and echoing no input.
            throw new UnsafeInputJsonException();
        }

        return normalised;
    }

    // Responses pass through untouched. This converter sits on the shared options, so it is in the write
    // path too, and re-running the read-side policy over stored data would silently rewrite what is already
    // saved rather than validating anything.
    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value);
    }

    // JsonConverter<T>'s default property-name implementations throw NotSupportedException. Without these
    // two overrides, registering any string converter breaks every Dictionary<string, T> the API
    // serialises.
    public override string ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.GetString() ?? string.Empty;
    }

    public override void WriteAsPropertyName(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        writer.WritePropertyName(value);
    }
}

/// <summary>
/// Raised when a request string contains a character no legitimate caller sends. A distinct type so the
/// security-event classifier can tell a hostile payload apart from an ordinary parse failure without
/// matching on message text.
/// </summary>
public sealed class UnsafeInputJsonException() : JsonException(ErrorMessage)
{
    /// <summary>
    /// Also the wire message. MVC's input formatter records a deserialisation failure by storing the
    /// exception's message and discarding the exception itself, so this constant is the only handle
    /// InvalidModelStateResponse has for telling our rejection apart from a parser's — and it is safe to
    /// return to the caller, naming no type, path or offset.
    /// </summary>
    public const string ErrorMessage = "This value contains characters that are not allowed.";
}