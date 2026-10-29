using FluentValidation;

namespace Application.DataCollectionForms.Commands.RestoreFormModuleVersion;

public class RestoreFormModuleVersionCommandValidator : AbstractValidator<RestoreFormModuleVersionCommand>
{
    public RestoreFormModuleVersionCommandValidator()
    {
        RuleFor(x => x.ModuleId).NotEmpty();
        RuleFor(x => x.VersionNumber).GreaterThan(0);
    }
}