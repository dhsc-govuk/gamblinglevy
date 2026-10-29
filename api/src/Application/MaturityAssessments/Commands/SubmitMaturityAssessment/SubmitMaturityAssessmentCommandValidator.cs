using FluentValidation;

namespace Application.MaturityAssessments.Commands.SubmitMaturityAssessment;

public class SubmitMaturityAssessmentCommandValidator : AbstractValidator<SubmitMaturityAssessmentCommand>
{
    public SubmitMaturityAssessmentCommandValidator()
    {
        RuleFor(x => x.CollectionId).NotEmpty();
    }
}