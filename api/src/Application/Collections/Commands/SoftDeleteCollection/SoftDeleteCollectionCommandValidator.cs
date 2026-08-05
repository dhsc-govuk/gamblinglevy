using FluentValidation;

namespace Application.Collections.Commands.SoftDeleteCollection;

public class SoftDeleteCollectionCommandValidator : AbstractValidator<SoftDeleteCollectionCommand>
{
    public SoftDeleteCollectionCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}