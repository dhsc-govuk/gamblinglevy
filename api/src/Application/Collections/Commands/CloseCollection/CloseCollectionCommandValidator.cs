using FluentValidation;

namespace Application.Collections.Commands.CloseCollection;

public class CloseCollectionCommandValidator : AbstractValidator<CloseCollectionCommand>
{
    public CloseCollectionCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}