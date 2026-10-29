using Application.Common.Validation;
using Domain.ValueObjects;
using FluentValidation;

namespace Application.Users.Commands.OrganisationUsers;

public class CreateOrganisationUserCommandValidator : AbstractValidator<CreateOrganisationUserCommand>
{
    public CreateOrganisationUserCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty();
        RuleFor(x => x.Email).MaximumLength(FieldLimits.Email);

        // A bounded but unvalidated email address is textbook CWE-20, and the admin equivalents
        // (CreateAdminCommandValidator, AdminEditUserCommandValidator) have carried exactly this rule all along — the asymmetry was
        // the bug. Note this is a behaviour change: an organisation user can no longer be created with a
        // malformed address, which the invitation email would have silently failed to reach anyway.
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrEmpty(x.Email));

        RuleFor(x => x.FirstName).NotEmpty();
        RuleFor(x => x.FirstName).MaximumLength(FieldLimits.Name);
        RuleFor(x => x.LastName).NotEmpty();
        RuleFor(x => x.LastName).MaximumLength(FieldLimits.Name);
        RuleFor(x => x.OrganisationId).NotEmpty();
        RuleFor(x => x.Role)
            .NotEmpty()
            .Must(UserRole.IsSupported)
            .WithMessage("Role '{PropertyValue}' is not a supported role.");
    }
}