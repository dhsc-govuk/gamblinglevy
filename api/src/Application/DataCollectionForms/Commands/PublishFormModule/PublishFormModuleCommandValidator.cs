using FluentValidation;

namespace Application.DataCollectionForms.Commands.PublishFormModule;

public class PublishFormModuleCommandValidator : AbstractValidator<PublishFormModuleCommand>
{
    public PublishFormModuleCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}