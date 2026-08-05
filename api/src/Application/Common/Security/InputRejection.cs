using Application.Common.Validation;
using Domain.Users;

namespace Application.Common.Security;

/// <summary>
/// How seriously to take a rejected request.
/// </summary>
public enum InputRejectionTier
{
    /// <summary>An ordinary rule failure — a missing name, a number out of range, a typo. Logged, not audited.</summary>
    Field,

    /// <summary>Input a conforming client cannot produce. Logged and written to audit_logs.</summary>
    Security,
}

/// <summary>
/// One reason a request was rejected: which field, and what kind of thing was wrong with it.
///
/// The value itself is never carried. It is the caller's payload — recording it would move an attempted
/// injection string, and any personal data that happened to be in the same field, into the telemetry and
/// audit stores.
/// </summary>
public record InputRejectionReason(string Field, string Kind);

/// <summary>
/// Everything known about a rejected request at the point it is recorded.
/// </summary>
public record InputRejection(
    Guid RejectionId,
    string Endpoint,
    UserId? ActorId,
    string IpAddress,
    string DeviceLabel,
    IReadOnlyList<InputRejectionReason> Reasons)
{
    public InputRejectionTier Tier => InputRejectionClassifier.Classify(Reasons);
}

/// <summary>
/// Decides which tier a rejection belongs to, as a pure function so the policy can be tested without a
/// database and changed without touching any plumbing (BLUM-7240 AC8).
/// </summary>
public static class InputRejectionClassifier
{
    /// <summary>A field the request DTO does not declare — a mass-assignment probe.</summary>
    public const string UnknownFieldKind = "unknown_field";

    /// <summary>A null byte, a control character, or a bidirectional override.</summary>
    public const string UnsafeCharacterKind = "unsafe_character";

    /// <summary>A value that could not be parsed as the declared type.</summary>
    public const string MalformedValueKind = "malformed_value";

    /// <summary>An ordinary rule failure: required, too long, out of range, not in the allow-list.</summary>
    public const string FieldRuleKind = "field_rule";

    public static InputRejectionTier Classify(IReadOnlyList<InputRejectionReason> reasons)
    {
        return reasons.Any(reason => reason.Kind is UnknownFieldKind or UnsafeCharacterKind)
            ? InputRejectionTier.Security
            : InputRejectionTier.Field;
    }

    /// <summary>
    /// Maps a FluentValidation error code onto a kind. Codes carrying the SECURITY_ prefix are the
    /// validators' way of saying "a conforming client could not have sent this".
    /// </summary>
    public static string KindForErrorCode(string? errorCode)
    {
        return errorCode switch
        {
            ErrorCodes.ControlCharacter => UnsafeCharacterKind,
            ErrorCodes.UnknownField => UnknownFieldKind,
            _ => ErrorCodes.IsSecurityRelevant(errorCode) ? UnsafeCharacterKind : FieldRuleKind,
        };
    }
}