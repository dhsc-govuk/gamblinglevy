using FluentValidation;

namespace Application.Organisations.Commands.SuspendLocalAuthority;

public class SuspendLocalAuthorityCommandValidator : AbstractValidator<SuspendLocalAuthorityCommand>
{
    public SuspendLocalAuthorityCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}