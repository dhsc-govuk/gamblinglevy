using FluentValidation;

namespace Application.Submissions.Commands.SaveActivityReport;

public class SaveActivityReportCommandValidator : AbstractValidator<SaveActivityReportCommand>
{
    public SaveActivityReportCommandValidator()
    {
        RuleFor(x => x.CollectionId).NotEmpty();
        RuleFor(x => x.ActivityId).NotEmpty();
        RuleFor(x => x.Answers).NotNull();
    }
}