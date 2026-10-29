using System.Text.RegularExpressions;
using Application.Common.Funding;
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
    /// The optional-money form of <see cref="PositiveMoney{T}(IRuleBuilder{T, decimal}, string)"/>: null passes, a
    /// supplied value is bounded identically. Declared against the nullable property itself rather than as
    /// PositiveMoney over x.Value under a .When(...), because the latter reports the failure under the property
    /// name "NextYearAllocatedAmount.Value" and the client's fieldNameMap would no longer route it onto the field.
    /// </summary>
    public static IRuleBuilderOptions<T, decimal?> PositiveMoney<T>(this IRuleBuilder<T, decimal?> rule, string label)
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

    // A Local Authority's ONS/GSS code: a country-prefix letter (E, W, S or N) followed by eight digits,
    // e.g. E06000001. BLUM-7663 — mirrors the Family Hubs project's OnsCodePattern. Neither codebase holds an
    // enumerated reference list of real ONS codes, so this checks shape rather than list membership.
    // Case-insensitive: OrganisationRepository.FindByOnsCodeAsync already treats ONS codes as
    // case-insensitive for duplicate detection, so a lowercase-but-otherwise-valid code must still reach that
    // check rather than being turned away here first.
    // \A/\z rather than ^/$: in .NET, unquantified $ matches before a trailing \n as well as at the true end
    // of the string, so "E06000001\n" would otherwise pass a check this rule claims enforces the exact shape.
    private static readonly Regex OnsCodePattern = new(@"\A[EWSN][0-9]{8}\z", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// The ONS/GSS code shape for a Local Authority geography. Declared on top of MaximumLength(FieldLimits.OnsCode)
    /// rather than replacing it, so the column-width guard survives if the shape check is ever loosened.
    /// </summary>
    public static IRuleBuilderOptions<T, string?> OnsCodeFormat<T>(this IRuleBuilder<T, string?> rule)
    {
        return rule
            .MaximumLength(FieldLimits.OnsCode)
            .WithErrorCode(ErrorCodes.MaxLength)
            .WithMessage($"Must be {FieldLimits.OnsCode} characters or fewer.")
            .Matches(OnsCodePattern)
            .WithErrorCode(ErrorCodes.NotAllowed)
            .WithMessage("ONS code is not recognised. Enter it in the format E06000001 (a letter followed by 8 numbers).");
    }

    /// <summary>
    /// A "YYYY/YY" financial year, matching FinancialYearRange. Every lookup that reaches
    /// funding_windows.financial_year matches by exact string equality, so a value in any other shape does not
    /// fail — it silently resolves to a window nothing else agrees with, or (via IFundingWindowResolver) creates
    /// one. Bounded by FieldLimits.FinancialYear as well, so the column width is still covered if the shape check
    /// is ever loosened.
    /// </summary>
    public static IRuleBuilderOptions<T, string?> FinancialYearFormat<T>(this IRuleBuilder<T, string?> rule)
    {
        return rule
            .MaximumLength(FieldLimits.FinancialYear)
            .WithErrorCode(ErrorCodes.MaxLength)
            .WithMessage($"Must be {FieldLimits.FinancialYear} characters or fewer.")
            .Must(FinancialYearRange.IsValid)
            .WithErrorCode(ErrorCodes.NotAllowed)
            .WithMessage("Must be a financial year in the format 2026/27.");
    }

    /// <summary>
    /// An optional date whose year must fall between FieldLimits.MinDateYear and MaxDateYear. DateOnly accepts any
    /// year up to 9999, so without this a mistyped 5050 is saved (BLUM-8053). Null passes, because a draft may not
    /// have answered the question yet. The label leads the message because it reaches the admin verbatim as GOV.UK
    /// copy, e.g. "Year collection opens".
    /// </summary>
    public static IRuleBuilderOptions<T, DateOnly?> YearInRange<T>(this IRuleBuilder<T, DateOnly?> rule, string label)
    {
        return rule
            .Must(date => date is null || (date.Value.Year >= FieldLimits.MinDateYear && date.Value.Year <= FieldLimits.MaxDateYear))
            .WithErrorCode(ErrorCodes.OutOfRange)
            .WithMessage($"{label} must be between {FieldLimits.MinDateYear} and {FieldLimits.MaxDateYear}");
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