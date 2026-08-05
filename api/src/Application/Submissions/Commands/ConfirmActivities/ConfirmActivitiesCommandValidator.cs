using FluentValidation;

namespace Application.Submissions.Commands.ConfirmActivities;

public class ConfirmActivitiesCommandValidator : AbstractValidator<ConfirmActivitiesCommand>
{
    public ConfirmActivitiesCommandValidator()
    {
        RuleFor(x => x.CollectionId).NotEmpty();
    }
}