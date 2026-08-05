using FluentValidation;

namespace Application.Activities.Commands.RestoreActivity;

public class RestoreActivityCommandValidator : AbstractValidator<RestoreActivityCommand>
{
    public RestoreActivityCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}