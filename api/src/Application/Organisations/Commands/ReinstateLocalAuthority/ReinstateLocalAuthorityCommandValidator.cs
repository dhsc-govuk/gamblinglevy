using FluentValidation;

namespace Application.Organisations.Commands.ReinstateLocalAuthority;

public class ReinstateLocalAuthorityCommandValidator : AbstractValidator<ReinstateLocalAuthorityCommand>
{
    public ReinstateLocalAuthorityCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}