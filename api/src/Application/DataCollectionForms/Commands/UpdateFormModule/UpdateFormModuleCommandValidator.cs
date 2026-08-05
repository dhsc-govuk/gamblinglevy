using FluentValidation;

namespace Application.DataCollectionForms.Commands.UpdateFormModule;

public class UpdateFormModuleCommandValidator : AbstractValidator<UpdateFormModuleCommand>
{
    public UpdateFormModuleCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(255);
        RuleFor(x => x.Description).MaximumLength(4000).When(x => !string.IsNullOrWhiteSpace(x.Description));
    }
}