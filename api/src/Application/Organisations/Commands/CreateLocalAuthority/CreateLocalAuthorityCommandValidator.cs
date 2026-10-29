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
        // Optional (the handler falls back to the current FY), but checked when supplied: the financial year
        // selects the funding window by exact string equality, so a malformed value silently creates a window
        // nothing else in the system matches rather than failing (BLUM-7608).
        RuleFor(x => x.FinancialYear)
            .FinancialYearFormat()
            .When(x => !string.IsNullOrWhiteSpace(x.FinancialYear));
        // The upcoming year's allocation is optional - DHSC may not know the figure at onboarding - but when it
        // is supplied it is bounded exactly like the current year's.
        RuleFor(x => x.NextYearAllocatedAmount).PositiveMoney("Annual Allocation");
        // An ONS code identifies a Local Authority geography, so only LAs have one — requiring it of a VCSE
        // would make that organisation type impossible to create (BLUM-7051). The message is set explicitly
        // rather than left to FluentValidation's default so the client can surface it verbatim.
        RuleFor(x => x.OnsCode)
            .NotEmpty()
            .WithMessage("Enter the ONS code")
            .When(x => x.Type == OrgTypes.LocalAuthority);
        // BLUM-7663: validated against the ONS reference list — in practice its shape, since neither this
        // codebase nor Family Hubs (which this mirrors) holds an enumerated list of real ONS codes.
        RuleFor(x => x.OnsCode)
            .OnsCodeFormat()
            .When(x => !string.IsNullOrWhiteSpace(x.OnsCode));
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
        // BLUM-7945: the admin must choose "Activate it now" or "Leave it deactivated" - there is no default,
        // because either one silently picked would decide whether the organisation's users can sign in. Message
        // matches the client's own check on the radio so both read the same.
        RuleFor(x => x.ActivateImmediately)
            .NotNull()
            .WithMessage("Select when you want to activate this account");
    }
}