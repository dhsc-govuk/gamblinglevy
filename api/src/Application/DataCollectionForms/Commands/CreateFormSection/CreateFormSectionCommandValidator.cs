using FluentValidation;

namespace Application.DataCollectionForms.Commands.CreateFormSection;

public class CreateFormSectionCommandValidator : AbstractValidator<CreateFormSectionCommand>
{
    public CreateFormSectionCommandValidator()
    {
        RuleFor(x => x.FormModuleId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(255);
        RuleFor(x => x.Description).MaximumLength(4000).When(x => !string.IsNullOrWhiteSpace(x.Description));
        RuleFor(x => x.HelpText).MaximumLength(4000).When(x => !string.IsNullOrWhiteSpace(x.HelpText));
    }
}