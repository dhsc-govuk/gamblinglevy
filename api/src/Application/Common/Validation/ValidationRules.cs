using Application.Common.Text;
using FluentValidation;

namespace Application.Common.Validation;

/// <summary>
/// The shared rule vocabulary for command validators.
///
/// Before BLUM-7240 every validator restated its own rules: MaximumLength appeared in only 23 of 59
/// validator classes, allow-lists were duplicated as a private string[] per validator (the activity status
/// list existed in four places), and there was no upper bound on any money field. These extensions exist so
/// a rule is declared once and a new validator gets the bounded, allow-listed behaviour by default.
///
/// Written as extension methods rather than a base AbstractValidator so they compose with the existing
/// .When(...) and RuleForEach(...).ChildRules(...) chains without an inheritance change across sixty files —
/// and, importantly, so the failure is still reported under the original property name, which is what the
/// client's fieldNameMap keys on.
/// </summary>
public static class ValidationRules
{
    /// <summary>
    /// Free text bounded to its column.
    ///
    /// The length is measured on the trimmed value because that is what the handlers persist — a name that
    /// exactly fills its column but arrives with a trailing newline would otherwise fail a limit it
    /// actually fits.
    /// </summary>
    public static IRuleBuilderOptions<T, string?> SafeText<T>(this IRuleBuilder<T, string?> rule, int max)
    {
        return rule
            .Must(value => value is null || value.Trim().Length <= max)
            .WithErrorCode(ErrorCodes.MaxLength)
            .WithMessage($"Must be {max} characters or fewer.")

            // Defence in depth. For a JSON body the primary check is SanitisedStringJsonConverter, which
            // rejects these characters before binding; this covers any command built from another source
            // and keeps the rule true of the command rather than of one transport.
            .Must(value => value is null || !RequestStringNormaliser.ContainsRejectedCharacter(value))
            .WithErrorCode(ErrorCodes.ControlCharacter)
            .WithMessage("Contains characters that are not allowed.");
    }

    /// <summary>
    /// A machine-readable identifier: bounded, and restricted to the character class the generators produce.
    /// </summary>
    public static IRuleBuilderOptions<T, string?> SafeCode<T>(this IRuleBuilder<T, string?> rule, int max)
    {
        return rule
            .SafeText(max)
            .Matches("^[A-Z0-9_]+$")
            .WithErrorCode(ErrorCodes.NotAllowed)
            .WithMessage("A code can only contain capital letters, numbers and underscores.");
    }

    /// <summary>
    /// An allow-list. Takes the collection rather than a params array so callers pass the single shared
    /// declaration on the domain value object instead of restating it.
    /// </summary>
    public static IRuleBuilderOptions<T, string?> AllowedValue<T>(
        this IRuleBuilder<T, string?> rule,
        Func<string?, bool> isSupported,
        string message)
    {
        return rule
            .Must(value => isSupported(value))
            .WithErrorCode(ErrorCodes.NotAllowed)
            .WithMessage(message);
    }

    /// <summary>
    /// A numeric(18,2) money column: bounded above as well as below, and scale-checked so more than two
    /// decimal places is a 400 rather than a silent round. Mirrors FundingSpendValidator, which already
    /// does this for spend entries.
    ///
    /// The label is part of the message rather than left to FluentValidation's default because the client
    /// matches on the wording — client/src/views/Admin/Organisations/errors.ts keys its GDS copy off
    /// "greater than 0" — so the existing sentence has to survive verbatim.
    /// </summary>
    public static IRuleBuilderOptions<T, decimal> PositiveMoney<T>(this IRuleBuilder<T, decimal> rule, string label)
    {
        return rule
            .GreaterThan(0)
            .WithErrorCode(ErrorCodes.OutOfRange)
            .WithMessage($"{label} must be greater than 0.")
            .LessThanOrEqualTo(FieldLimits.MaxMoney)
            .WithErrorCode(ErrorCodes.OutOfRange)
            .WithMessage($"{label} must be {FieldLimits.MaxMoney:N0} or less.")
            .PrecisionScale(FieldLimits.MoneyPrecision, FieldLimits.MoneyScale, ignoreTrailingZeros: true)
            .WithErrorCode(ErrorCodes.OutOfRange)
            .WithMessage($"{label} must have no more than 2 decimal places.");
    }

    /// <summary>
    /// Bounds a list carried in a request body. An unbounded list is an allocation cost the caller chooses.
    /// </summary>
    public static IRuleBuilderOptions<T, IReadOnlyList<TItem>> Bounded<T, TItem>(
        this IRuleBuilder<T, IReadOnlyList<TItem>> rule,
        int max)
    {
        return rule
            .Must(items => items is null || items.Count <= max)
            .WithErrorCode(ErrorCodes.OutOfRange)
            .WithMessage($"Must contain {max} items or fewer.");
    }

    /// <summary>
    /// Rejects a reorder payload that repeats an id. Display order is derived from the list index, so a
    /// duplicate does not fail — it silently reorders the wrong thing.
    /// </summary>
    public static IRuleBuilderOptions<T, IReadOnlyList<Guid>> DistinctIds<T>(
        this IRuleBuilder<T, IReadOnlyList<Guid>> rule)
    {
        return rule
            .Must(ids => ids is null || ids.Distinct().Count() == ids.Count)
            .WithErrorCode(ErrorCodes.Duplicate)
            .WithMessage("Must not contain the same id more than once.");
    }
}