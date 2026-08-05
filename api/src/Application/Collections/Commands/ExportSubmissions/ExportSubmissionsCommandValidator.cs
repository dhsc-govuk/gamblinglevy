using FluentValidation;

namespace Application.Collections.Commands.ExportSubmissions;

public class ExportSubmissionsCommandValidator : AbstractValidator<ExportSubmissionsCommand>
{
    public ExportSubmissionsCommandValidator()
    {
        RuleFor(x => x.CollectionId).NotEmpty();
    }
}