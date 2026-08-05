using FluentValidation;

namespace Application.Activities.Commands.SoftDeleteActivity;

public class SoftDeleteActivityCommandValidator : AbstractValidator<SoftDeleteActivityCommand>
{
    public SoftDeleteActivityCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}