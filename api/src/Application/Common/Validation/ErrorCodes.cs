namespace Application.Common.Validation;

/// <summary>
/// Error codes attached to validation failures.
///
/// FluentValidation already carries an ErrorCode on every failure and ValidationBehaviour already forwards
/// it to telemetry as "rule", so this is the classification channel that already exists rather than a new
/// one. Codes beginning <see cref="SecurityPrefix"/> mark a failure as something a conforming client cannot
/// produce, which is what promotes it from a logged event to an audit_logs row (BLUM-7240 AC8).
///
/// The prefix is a cross-layer contract between the validators here and ValidationExceptionMiddleware.
/// Renaming it silently stops the security trail being written, without breaking anything visible.
/// </summary>
public static class ErrorCodes
{
    public const string SecurityPrefix = "SECURITY_";

    public const string ControlCharacter = SecurityPrefix + "CONTROL_CHARACTER";
    public const string UnknownField = SecurityPrefix + "UNKNOWN_FIELD";

    public const string MaxLength = "MAX_LENGTH";
    public const string NotAllowed = "NOT_ALLOWED";
    public const string OutOfRange = "OUT_OF_RANGE";
    public const string Duplicate = "DUPLICATE";

    public static bool IsSecurityRelevant(string? errorCode)
    {
        return errorCode is not null && errorCode.StartsWith(SecurityPrefix, StringComparison.Ordinal);
    }
}