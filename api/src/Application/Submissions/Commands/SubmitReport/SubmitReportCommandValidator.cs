using FluentValidation;

namespace Application.Submissions.Commands.SubmitReport;

public class SubmitReportCommandValidator : AbstractValidator<SubmitReportCommand>
{
    public SubmitReportCommandValidator()
    {
        RuleFor(x => x.CollectionId).NotEmpty();
    }
}