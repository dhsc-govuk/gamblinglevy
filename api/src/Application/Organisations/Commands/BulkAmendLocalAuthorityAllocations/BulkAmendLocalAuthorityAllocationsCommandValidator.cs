using Application.Common.Validation;
using FluentValidation;

namespace Application.Organisations.Commands.BulkAmendLocalAuthorityAllocations;

/// <summary>
/// See BulkImportLocalAuthoritiesCommandValidator: the per-row rules stay in the handler, which owns the
/// structured envelope the client parses. Only the size of the upload itself is bounded here, because
/// CsvParser reads the whole file before any row is examined (BLUM-7240).
/// </summary>
public class BulkAmendLocalAuthorityAllocationsCommandValidator : AbstractValidator<BulkAmendLocalAuthorityAllocationsCommand>
{
    public BulkAmendLocalAuthorityAllocationsCommandValidator()
    {
        RuleFor(x => x.File)
            .Must(file => file is null || file.Length <= FieldLimits.MaxCsvUploadBytes)
            .WithErrorCode(ErrorCodes.OutOfRange)
            .WithMessage($"The file must be {FieldLimits.MaxCsvUploadBytes / (1024 * 1024)}MB or smaller.");
    }
}