using FluentValidation;

namespace Application.DataCollectionForms.Commands.CreateFormModule;

public class CreateFormModuleCommandValidator : AbstractValidator<CreateFormModuleCommand>
{
    public CreateFormModuleCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(255);
        RuleFor(x => x.Description).MaximumLength(4000).When(x => !string.IsNullOrWhiteSpace(x.Description));
    }
}