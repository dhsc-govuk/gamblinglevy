using FluentValidation;

namespace Application.Submissions.Commands.CompleteStrategicFeedback;

public class CompleteStrategicFeedbackCommandValidator : AbstractValidator<CompleteStrategicFeedbackCommand>
{
    public CompleteStrategicFeedbackCommandValidator()
    {
        RuleFor(x => x.CollectionId).NotEmpty();
    }
}