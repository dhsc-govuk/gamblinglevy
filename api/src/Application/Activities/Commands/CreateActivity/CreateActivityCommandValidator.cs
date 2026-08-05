using Application.Common.Validation;
using Domain.Activities;
using FluentValidation;

namespace Application.Activities.Commands.CreateActivity;

public class CreateActivityCommandValidator : AbstractValidator<CreateActivityCommand>
{
    public CreateActivityCommandValidator()
    {
        RuleFor(x => x.Title).MaximumLength(FieldLimits.Name).When(x => !string.IsNullOrWhiteSpace(x.Title));
        RuleFor(x => x.CustomTaskText).MaximumLength(FieldLimits.Name).When(x => !string.IsNullOrWhiteSpace(x.CustomTaskText));

        RuleFor(x => x.Status)
            .Must(ActivityStatus.IsSupported)
            .When(x => !string.IsNullOrWhiteSpace(x.Status))
            .WithMessage("Select a valid status.");

        // Mandatory-for-completion rules only apply when finishing setup (not when saving a draft). Category and
        // type are picked first; status is chosen on the final structural step. The per-category detail/expected-
        // results questions are enforced separately (answer validation + the client flow).
        When(x => !x.SaveAsDraft, () =>
        {
            RuleFor(x => x.CategoryId).NotNull().WithMessage("Select a category.");
            RuleFor(x => x.TaskId).NotNull().WithMessage("Select a type of work.");
            RuleFor(x => x.Status).NotEmpty().WithMessage("Select the stage the activity is at.");

            When(x => string.Equals(x.Status, ActivityStatuses.Active, StringComparison.OrdinalIgnoreCase), () =>
            {
                RuleFor(x => x.StartMonth).NotNull().InclusiveBetween(1, 12).WithMessage("Enter a start date, for example, 3 2026.");
                RuleFor(x => x.StartYear).NotNull().InclusiveBetween(2000, 2100).WithMessage("Enter a start date, for example, 3 2026.");

                // The start date must not be in the future. Guarded on the presence/range rules above so this
                // message never stacks on top of "enter a valid month/year". No trailing full stop, to match
                // the client-side copy the user actually sees (validateStartDate) and GDS error-message style.
                RuleFor(x => x.StartMonth)
                    .Must((command, _) => !ActivityStartDate.IsInFuture(command.StartYear, command.StartMonth))
                    .When(x => x.StartMonth is >= 1 and <= 12 && x.StartYear is >= 2000 and <= 2100)
                    .WithMessage("Start date must be this month or in the past");
            });
        });
    }
}