using Application.Common.Validation;
using Domain.Organisations;
using FluentValidation;

namespace Application.Organisations.Commands.UpdateLocalAuthority;

public class UpdateLocalAuthorityCommandValidator : AbstractValidator<UpdateLocalAuthorityCommand>
{
    public UpdateLocalAuthorityCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(FieldLimits.Name);
        RuleFor(x => x.Region)
            .NotEmpty()
            .Must(Region.IsSupported)
            .WithMessage("Region must be one of the supported English regions.");
        RuleFor(x => x.Type)
            .NotEmpty()
            .Must(OrgType.IsSupported)
            .WithMessage("Type must be LA or VCSE.");
        // Required for Local Authorities only — a VCSE organisation has no ONS code (BLUM-7051). Mirrors
        // CreateLocalAuthorityCommandValidator so an organisation cannot be created valid and then edited
        // into a state the create path would have rejected.
        RuleFor(x => x.OnsCode)
            .NotEmpty()
            .WithMessage("Enter the ONS code")
            .When(x => x.Type == OrgTypes.LocalAuthority);
        // BLUM-7663: validated against the ONS reference list — in practice its shape, since neither this
        // codebase nor Family Hubs (which this mirrors) holds an enumerated list of real ONS codes. Mirrors
        // CreateLocalAuthorityCommandValidator so an organisation cannot be edited into a shape create would reject.
        RuleFor(x => x.OnsCode)
            .OnsCodeFormat()
            .When(x => !string.IsNullOrWhiteSpace(x.OnsCode));
        // Required for every organisation type (BLUM-7050). Mirrors CreateLocalAuthorityCommandValidator so an
        // organisation cannot be created valid and then edited into a state the create path would have rejected.
        // Organisations that predate the rule carry no E-code, so their first edit has to supply one.
        RuleFor(x => x.ECode).NotEmpty().WithMessage("Enter the E-code");
        // Mirrors CreateLocalAuthorityCommandValidator: measured on the trimmed value, with explicit copy so the
        // client never surfaces FluentValidation's default message naming the C# property.
        RuleFor(x => x.ECode)
            .Must(e => e!.Trim().Length <= Organisation.ECodeMaxLength)
            .WithMessage($"E-code must be {Organisation.ECodeMaxLength} characters or fewer")
            .When(x => !string.IsNullOrWhiteSpace(x.ECode));
    }
}