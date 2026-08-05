using FluentValidation;

namespace Application.Forms.Commands.DeleteSection;

public class DeleteSectionCommandValidator : AbstractValidator<DeleteSectionCommand>
{
    public DeleteSectionCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}