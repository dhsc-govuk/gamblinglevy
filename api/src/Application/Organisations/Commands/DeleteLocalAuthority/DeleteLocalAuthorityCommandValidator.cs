using FluentValidation;

namespace Application.Organisations.Commands.DeleteLocalAuthority;

public class DeleteLocalAuthorityCommandValidator : AbstractValidator<DeleteLocalAuthorityCommand>
{
    public DeleteLocalAuthorityCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}