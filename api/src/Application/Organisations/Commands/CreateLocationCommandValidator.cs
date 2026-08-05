using FluentValidation;

namespace Application.Organisations.Commands;

public class CreateLocationCommandValidator : AbstractValidator<CreateLocationCommand>
{
    public CreateLocationCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty();
        RuleFor(x => x.Name).MaximumLength(255);
        RuleFor(x => x.OrganisationId).NotEmpty();
    }
}