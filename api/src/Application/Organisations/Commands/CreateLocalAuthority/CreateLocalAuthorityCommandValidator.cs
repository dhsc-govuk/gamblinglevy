using Application.Common.Validation;
using Domain.Organisations;
using FluentValidation;

namespace Application.Organisations.Commands.CreateLocalAuthority;

public class CreateLocalAuthorityCommandValidator : AbstractValidator<CreateLocalAuthorityCommand>
{
    public CreateLocalAuthorityCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(FieldLimits.Name);
        RuleFor(x => x.Region)
            .NotEmpty()
            .Must(Region.IsSupported)
            .WithMessage("Region must be one of the supported English regions.");
        RuleFor(x => x.Type)
            .NotEmpty()
            .Must(OrgType.IsSupported)
            .WithMessage("Type must be LA or VCSE.");

        // See AmendLocalAuthorityAllocationCommandValidator: GreaterThan(0) alone left the numeric(18,2)
        // column's upper bound and scale unguarded (BLUM-7240).
        RuleFor(x => x.AllocatedAmount).PositiveMoney("Annual Allocation");
        // An ONS code identifies a Local Authority geography, so only LAs have one — requiring it of a VCSE
        // would make that organisation type impossible to create (BLUM-7051). The message is set explicitly
        // rather than left to FluentValidation's default so the client can surface it verbatim.
        RuleFor(x => x.OnsCode)
            .NotEmpty()
            .WithMessage("Enter the ONS code")
            .When(x => x.Type == OrgTypes.LocalAuthority);
        RuleFor(x => x.OnsCode).MaximumLength(FieldLimits.OnsCode);
        // Unconditional, unlike OnsCode above: an E-code is the organisation's DHSC finance identifier, which
        // every organisation receiving levy money has regardless of type, so there is no LA/VCSE condition to
        // apply (BLUM-7050). The message is set explicitly so the client can surface it verbatim.
        RuleFor(x => x.ECode).NotEmpty().WithMessage("Enter the E-code");
        // Measured on the trimmed value, because that is what the handler persists — a 64-character code pasted
        // with a trailing newline otherwise fails a limit it actually fits. Message set explicitly so the client
        // never has to surface FluentValidation's default, which names the C# property ("ECode") to the user.
        // Must(...) rather than MaximumLength(...) on a projected expression: the rule has to stay declared
        // against ECode itself so the failure is still reported under that property name, which is the key the
        // client's fieldNameMap uses to route the error onto the field instead of collapsing it onto Save.
        RuleFor(x => x.ECode)
            .Must(e => e!.Trim().Length <= Organisation.ECodeMaxLength)
            .WithMessage($"E-code must be {Organisation.ECodeMaxLength} characters or fewer")
            .When(x => !string.IsNullOrWhiteSpace(x.ECode));
    }
}