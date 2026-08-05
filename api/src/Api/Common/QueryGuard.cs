using Application.Common.Validation;
using FluentValidation;
using FluentValidation.Results;

namespace Api.Common;

/// <summary>
/// Validates query-string parameters on the read endpoints.
///
/// Reads bypass MediatR in this codebase — they go straight from the controller to an I{Aggregate}Queries
/// method — so no IValidator ever runs for them and the whole command-side validation pipeline is simply
/// absent. Before BLUM-7240 that meant ?onboardingStatus=bogus reached OnboardingStatus.From inside a
/// repository Where clause and came back as a 500, and ?pageSize=100000 came back as 200 OK with a
/// silently clamped page.
///
/// This throws the same FluentValidation.ValidationException the MediatR pipeline throws, which
/// ValidationExceptionMiddleware already catches from anywhere downstream. The envelope is therefore
/// identical to the command path by construction rather than by convention — which is what AC7's
/// "server-side rules apply identically" actually asks for.
/// </summary>
public static class QueryGuard
{
    public static void Against(params (bool IsInvalid, string Property, string Message)[] rules)
    {
        var failures = rules
            .Where(rule => rule.IsInvalid)
            .Select(rule => new ValidationFailure(rule.Property, rule.Message) { ErrorCode = ErrorCodes.OutOfRange })
            .ToList();

        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }
    }

    /// <summary>
    /// MaxPage is not arbitrary: the repository computes Skip((page - 1) * pageSize) in an int, so a large
    /// enough page overflows into a negative OFFSET rather than an empty result.
    /// </summary>
    public static void Paging(int page, int pageSize)
    {
        Against(
            (page < 1 || page > FieldLimits.MaxPage, "page", $"page must be between 1 and {FieldLimits.MaxPage}."),
            (pageSize < FieldLimits.MinPageSize || pageSize > FieldLimits.MaxPageSize, "pageSize", $"pageSize must be between {FieldLimits.MinPageSize} and {FieldLimits.MaxPageSize}."));
    }

    /// <summary>
    /// An optional filter drawn from a fixed set. Absent is always allowed — these parameters narrow a list
    /// rather than identify a record.
    /// </summary>
    public static void AllowedValue(string? value, Func<string?, bool> isSupported, string property, string message)
    {
        Against((!string.IsNullOrEmpty(value) && !isSupported(value), property, message));
    }

    public static void MaxLength(string? value, int max, string property)
    {
        Against((value is not null && value.Length > max, property, $"{property} must be {max} characters or fewer."));
    }
}