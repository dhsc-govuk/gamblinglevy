using Domain.Organisations;
using FluentValidation;

namespace Application.Collections.Commands.ExportSubmissions;

public class ExportSubmissionsCommandValidator : AbstractValidator<ExportSubmissionsCommand>
{
    public ExportSubmissionsCommandValidator()
    {
        RuleFor(x => x.CollectionId).NotEmpty();

        // Absent region means every organisation, so only a value that is actually present is checked. The controller
        // normalises "?region=" to null before this runs, so anything reaching here is a real choice.
        //
        // Not QueryGuard.AllowedValue, despite region arriving as a query-string parameter: QueryGuard exists because
        // READS bypass MediatR and no IValidator runs for them. The export is a command, so ValidationBehaviour is live
        // and this validator is its conventional home. Both produce the identical validation_failed envelope.
        //
        // The wording is the one CreateLocalAuthorityCommandValidator already uses, because
        // client/src/views/Admin/Organisations/errors.ts string-matches "supported English regions" to pick its GDS
        // copy — one phrasing for one rule.
        RuleFor(x => x.Region)
            .Must(Region.IsSupported)
            .WithMessage("Region must be one of the supported English regions.")
            .When(x => x.Region is not null);
    }
}