using FluentValidation;

namespace Application.Submissions.Commands.SaveStrategicFeedback;

public class SaveStrategicFeedbackCommandValidator : AbstractValidator<SaveStrategicFeedbackCommand>
{
    public SaveStrategicFeedbackCommandValidator()
    {
        RuleFor(x => x.CollectionId).NotEmpty();
    }
}