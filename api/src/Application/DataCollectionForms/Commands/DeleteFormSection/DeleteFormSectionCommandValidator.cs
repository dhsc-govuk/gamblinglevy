using FluentValidation;

namespace Application.DataCollectionForms.Commands.DeleteFormSection;

public class DeleteFormSectionCommandValidator : AbstractValidator<DeleteFormSectionCommand>
{
    public DeleteFormSectionCommandValidator()
    {
        RuleFor(x => x.FormModuleId).NotEmpty();
        RuleFor(x => x.SectionId).NotEmpty();
    }
}