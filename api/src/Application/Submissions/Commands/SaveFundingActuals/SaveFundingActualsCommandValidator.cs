using FluentValidation;

namespace Application.Submissions.Commands.SaveFundingActuals;

public class SaveFundingActualsCommandValidator : AbstractValidator<SaveFundingActualsCommand>
{
    public SaveFundingActualsCommandValidator()
    {
        RuleFor(x => x.CollectionId).NotEmpty();
        RuleFor(x => x.Activities).NotNull();
    }
}