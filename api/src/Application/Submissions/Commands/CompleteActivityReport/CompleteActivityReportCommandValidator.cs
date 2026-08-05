using FluentValidation;

namespace Application.Submissions.Commands.CompleteActivityReport;

public class CompleteActivityReportCommandValidator : AbstractValidator<CompleteActivityReportCommand>
{
    public CompleteActivityReportCommandValidator()
    {
        RuleFor(x => x.CollectionId).NotEmpty();
        RuleFor(x => x.ActivityId).NotEmpty();
    }
}