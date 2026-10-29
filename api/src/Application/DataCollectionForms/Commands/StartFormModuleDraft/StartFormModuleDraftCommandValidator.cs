using FluentValidation;

namespace Application.DataCollectionForms.Commands.StartFormModuleDraft;

public class StartFormModuleDraftCommandValidator : AbstractValidator<StartFormModuleDraftCommand>
{
    public StartFormModuleDraftCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}