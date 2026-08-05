using System.Text.Json;
using System.Text.Json.Serialization;

namespace Api.Common;

/// <summary>
/// The one 400 body this API emits for rejected input.
///
/// Until BLUM-7240 there were two. A FluentValidation failure came back as this shape, while a
/// model-binding or JSON failure came back as the framework's ValidationProblemDetails — a different
/// shape whose message named the .NET type, the JSON path and the byte position
/// ("The JSON value could not be converted to System.Decimal. Path: $.allocatedAmount | LineNumber: 0
/// | BytePositionInLine: 45."). The client carried three parsers to cope, and the binder-shaped one
/// leaked internals to anyone who sent a malformed body.
///
/// The JsonPropertyName attributes are load-bearing rather than decorative. ValidationExceptionMiddleware
/// writes this with JsonSerializer's default options, which apply no naming policy, while an ObjectResult
/// from InvalidModelStateResponse goes through MVC's web options, which camel-case. Without the attributes
/// the two paths would disagree on casing and the client would silently stop matching one of them.
/// </summary>
public sealed record ValidationErrorEnvelope(
    [property: JsonPropertyName("error")] string Error,
    [property: JsonPropertyName("errors")] IReadOnlyList<ValidationErrorItem> Errors)
{
    /// <summary>
    /// Matched literally by client/src/views/Admin/Collections/messages.ts. Renaming it breaks the
    /// client's error copy with no error of its own.
    /// </summary>
    public const string ValidationFailed = "validation_failed";

    public static ValidationErrorEnvelope From(IEnumerable<ValidationErrorItem> errors)
    {
        return new ValidationErrorEnvelope(ValidationFailed, errors.ToList());
    }

    public string ToJson()
    {
        return JsonSerializer.Serialize(this);
    }
}

/// <summary>
/// One rejected field. Property is the C# property name, not the JSON name — that is what
/// client/src/views/Admin/Organisations/errors.ts keys its fieldNameMap on, and it is why validators
/// there use Must(...) rather than MaximumLength(...) where the two would report different names.
/// </summary>
public sealed record ValidationErrorItem(
    [property: JsonPropertyName("property")] string Property,
    [property: JsonPropertyName("message")] string Message);