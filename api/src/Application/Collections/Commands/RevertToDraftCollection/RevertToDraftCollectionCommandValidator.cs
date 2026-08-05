using FluentValidation;

namespace Application.Collections.Commands.RevertToDraftCollection;

public class RevertToDraftCollectionCommandValidator : AbstractValidator<RevertToDraftCollectionCommand>
{
    public RevertToDraftCollectionCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}