using FluentValidation;

namespace Application.MaturityAssessments.Commands.StartMaturityAssessment;

public class StartMaturityAssessmentCommandValidator : AbstractValidator<StartMaturityAssessmentCommand>
{
    public StartMaturityAssessmentCommandValidator()
    {
        RuleFor(x => x.CollectionId).NotEmpty();
    }
}