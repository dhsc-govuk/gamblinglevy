namespace Api.Common;

/// <summary>
/// Marks an action whose [FromBody] JSON may also arrive Base64-encoded inside a <c>{ "payload": "…" }</c>
/// envelope, flagged by <c>X-Body-Encoding: base64</c>. EncodedJsonBodyMiddleware swaps the decoded JSON in
/// before model binding, so the action, its validators, the string sanitiser and the input-rejection audit all
/// see exactly what a plain JSON request would have sent.
///
/// It exists because DAC's gateway WAF answers 403 to ordinary answer text that LAs type (BLUM-8917). An opt-in
/// per action rather than a URL list, so a renamed route cannot silently fall out of it. Put it on writes whose
/// bodies carry free text, and send them through client/src/helpers/encodedJsonBody.ts.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class AcceptsEncodedJsonBodyAttribute : Attribute
{
    /// <summary>Matched by ENCODED_BODY_HEADER in client/src/helpers/encodedJsonBody.ts.</summary>
    public const string HeaderName = "X-Body-Encoding";

    public const string HeaderValue = "base64";

    /// <summary>The envelope property holding the Base64 of the UTF-8 JSON body.</summary>
    public const string PayloadProperty = "payload";
}