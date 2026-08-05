using FluentValidation;

namespace Application.Collections.Commands.RestoreCollection;

public class RestoreCollectionCommandValidator : AbstractValidator<RestoreCollectionCommand>
{
    public RestoreCollectionCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}