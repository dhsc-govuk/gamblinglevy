using Application.Common.Validation;
using FluentValidation;

namespace Application.Organisations.Commands.BulkImportLocalAuthorities;

/// <summary>
/// Deliberately thin. A missing file, a file that is not CSV, a header-only file and every per-row rule all
/// stay in the handler, which owns the BULK_IMPORT_* envelope and the per-row outcome list the client
/// renders — moving any of that here would change a live contract.
///
/// The one thing the handler cannot express is a bound on the request itself: CsvParser materialises the
/// whole file into memory before a single row is examined (BLUM-7240).
/// </summary>
public class BulkImportLocalAuthoritiesCommandValidator : AbstractValidator<BulkImportLocalAuthoritiesCommand>
{
    public BulkImportLocalAuthoritiesCommandValidator()
    {
        RuleFor(x => x.File)
            .Must(file => file is null || file.Length <= FieldLimits.MaxCsvUploadBytes)
            .WithErrorCode(ErrorCodes.OutOfRange)
            .WithMessage($"The file must be {FieldLimits.MaxCsvUploadBytes / (1024 * 1024)}MB or smaller.");
    }
}