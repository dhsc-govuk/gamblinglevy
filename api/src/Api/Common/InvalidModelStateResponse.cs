using Application.Common.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Api.Common;

/// <summary>
/// Answers a model-binding or JSON-deserialisation failure in the same envelope FluentValidation uses.
///
/// No parser-authored message survives contact with this class. System.Text.Json writes messages that name
/// the .NET type, the JSON path and the byte offset, and the binder writes messages that interpolate the
/// caller's own value; both used to be handed straight back to the caller. A message authored by a
/// validation attribute is safe and is kept verbatim, because the client string-matches "field is required"
/// to choose its own GDS copy (client/src/views/Admin/Organisations/errors.ts).
///
/// Note this runs instead of the action, not around it, so ValidationExceptionMiddleware never sees these
/// failures — the [ApiController] filter returns a result rather than throwing. That is why the envelope
/// has to be built in two places and why ValidationErrorEnvelope exists to keep them identical.
/// </summary>
public static class InvalidModelStateResponse
{
    /// <summary>
    /// Everything a parser has to say about why a value would not parse is an internal detail, so the
    /// caller gets this instead. They already know what they sent.
    /// </summary>
    public const string InvalidValue = "Enter a valid value.";

    /// <summary>
    /// Reported when the failure belongs to the request as a whole rather than to a field — a body that is
    /// not JSON at all, or a key too malformed to be worth echoing.
    /// </summary>
    public const string RequestProperty = "Request";

    // The key comes from the request body, so its content is the caller's to choose. It is read as a lookup
    // key rather than rendered, but it must not become a way to get arbitrary text into a response.
    private const int MaxPropertyLength = 100;

    public static IActionResult Create(ActionContext context)
    {
        var logger = context.HttpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(InvalidModelStateResponse));

        var items = new List<ValidationErrorItem>();
        var reasons = new List<InputRejectionReason>();

        foreach (var (key, entry) in context.ModelState)
        {
            foreach (var error in entry.Errors)
            {
                var kind = InputRejectionContext.KindForModelStateError(key, error);
                var message = MessageFor(error, kind);

                // The original is diagnostic and internal-only. It names the CLR type, the JSON path and
                // the byte offset — "The JSON value could not be converted to
                // Api.Dtos.CreateLocalAuthorityRequestDto. Path: $.allocatedAmount | LineNumber: 0 |
                // BytePositionInLine: 131." — which is precisely what used to reach the caller.
                if (!ReferenceEquals(message, error.ErrorMessage))
                {
                    logger.LogWarning(
                        error.Exception,
                        "Rejected malformed request payload at {ModelStateKey} for {RequestPath}: {Detail}",
                        key,
                        context.HttpContext.Request.Path,
                        error.ErrorMessage);
                }

                var property = ToClientProperty(key);
                items.Add(new ValidationErrorItem(property, message));
                reasons.Add(new InputRejectionReason(property, kind));
            }
        }

        if (items.Count == 0)
        {
            items.Add(new ValidationErrorItem(RequestProperty, InvalidValue));
            reasons.Add(new InputRejectionReason(RequestProperty, InputRejectionClassifier.MalformedValueKind));
        }

        return new RecordedValidationResult(ValidationErrorEnvelope.From(items), reasons);
    }

    /// <summary>
    /// Records the rejection for security monitoring, then writes the 400 (BLUM-7240 AC8).
    ///
    /// A result type rather than a call inside Create, because the factory is synchronous while recording is
    /// not.
    ///
    /// The order matters and is not merely tidy. Writing the response first lets the caller observe the 400
    /// before the audit row is committed — the response is streamed, so HttpClient returns as soon as it has
    /// read the body, whether or not this method has finished. Recording first makes the trail deterministic
    /// rather than dependent on how fast the caller reads.
    /// </summary>
    private sealed class RecordedValidationResult(
        ValidationErrorEnvelope envelope,
        IReadOnlyList<InputRejectionReason> reasons)
        : ObjectResult(envelope)
    {
        public override async Task ExecuteResultAsync(ActionContext context)
        {
            StatusCode = StatusCodes.Status400BadRequest;

            await RecordAsync(context.HttpContext, reasons);
            await base.ExecuteResultAsync(context);
        }

        /// <summary>
        /// See ValidationExceptionMiddleware.RecordAsync: the recorder's own never-throw guarantee does not
        /// cover resolving it or building its argument, and this runs on the way to a 400.
        /// </summary>
        private static async Task RecordAsync(HttpContext context, IReadOnlyList<InputRejectionReason> reasons)
        {
            if (InputRejectionContext.IsExempt(context))
            {
                return;
            }

            try
            {
                var recorder = context.RequestServices.GetService<IInputRejectionRecorder>();
                if (recorder is null)
                {
                    return;
                }

                // Not context.RequestAborted — see InputRejectionRecorder.RecordAsync.
                await recorder.RecordAsync(InputRejectionContext.Build(context, reasons), CancellationToken.None);
            }
            catch (Exception recordingFailure)
            {
                context.RequestServices.GetService<ILoggerFactory>()
                    ?.CreateLogger(typeof(InvalidModelStateResponse))
                    .LogError(recordingFailure, "Failed to record a rejected request for {RequestPath}", context.Request.Path);
            }
        }
    }

    /// <summary>
    /// What the caller is told. A message written by a validation attribute is kept — the client matches on
    /// "field is required" to pick its own GDS copy — and so is our own converter's, which is a constant we
    /// own and says something useful. Everything else the deserialiser wrote is replaced.
    ///
    /// Returns the original instance when it is kept, so the caller can tell by reference whether the
    /// message was substituted and log the original if so.
    /// </summary>
    private static string MessageFor(ModelError error, string kind)
    {
        if (kind == InputRejectionClassifier.UnsafeCharacterKind)
        {
            return UnsafeInputJsonException.ErrorMessage;
        }

        if (kind is InputRejectionClassifier.UnknownFieldKind or InputRejectionClassifier.MalformedValueKind)
        {
            return InvalidValue;
        }

        return string.IsNullOrEmpty(error.ErrorMessage) ? InvalidValue : error.ErrorMessage;
    }

    /// <summary>
    /// ModelState keys arrive in two dialects. A deserialisation failure is keyed by System.Text.Json's
    /// path, which is camel-cased and prefixed ("$.allocatedAmount", "$.reminders[0].days"); everything
    /// else — a validation attribute, a query parameter, a route value — is already keyed by the C# name.
    /// Both are folded onto the C# form, because that is what FluentValidation reports and what the client's
    /// fieldNameMap looks up.
    /// </summary>
    internal static string ToClientProperty(string modelStateKey)
    {
        var isJsonPath = modelStateKey.StartsWith("$.", StringComparison.Ordinal);
        var key = isJsonPath ? modelStateKey[2..] : modelStateKey;

        if (key.Length == 0)
        {
            return RequestProperty;
        }

        if (isJsonPath)
        {
            key = string.Join('.', key.Split('.', StringSplitOptions.RemoveEmptyEntries).Select(Capitalise));
        }

        return key.Length is 0 or > MaxPropertyLength || !IsPropertyShaped(key) ? RequestProperty : key;
    }

    private static string Capitalise(string segment)
    {
        return segment.Length == 0 ? segment : char.ToUpperInvariant(segment[0]) + segment[1..];
    }

    private static bool IsPropertyShaped(string value)
    {
        return value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '[' or ']');
    }
}