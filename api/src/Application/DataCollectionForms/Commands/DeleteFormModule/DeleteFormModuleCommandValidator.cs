using FluentValidation;

namespace Application.DataCollectionForms.Commands.DeleteFormModule;

public class DeleteFormModuleCommandValidator : AbstractValidator<DeleteFormModuleCommand>
{
    public DeleteFormModuleCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}