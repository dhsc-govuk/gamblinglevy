using FluentValidation;

namespace Application.Submissions.Commands.SaveFundingForecast;

public class SaveFundingForecastCommandValidator : AbstractValidator<SaveFundingForecastCommand>
{
    public SaveFundingForecastCommandValidator()
    {
        RuleFor(x => x.CollectionId).NotEmpty();
        RuleFor(x => x.Activities).NotNull();
        RuleFor(x => x.Other).NotNull();
    }
}