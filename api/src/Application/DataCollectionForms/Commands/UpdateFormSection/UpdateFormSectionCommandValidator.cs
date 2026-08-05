using FluentValidation;

namespace Application.DataCollectionForms.Commands.UpdateFormSection;

public class UpdateFormSectionCommandValidator : AbstractValidator<UpdateFormSectionCommand>
{
    public UpdateFormSectionCommandValidator()
    {
        RuleFor(x => x.FormModuleId).NotEmpty();
        RuleFor(x => x.SectionId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(255);
        RuleFor(x => x.Description).MaximumLength(4000).When(x => !string.IsNullOrWhiteSpace(x.Description));
        RuleFor(x => x.HelpText).MaximumLength(4000).When(x => !string.IsNullOrWhiteSpace(x.HelpText));
    }
}