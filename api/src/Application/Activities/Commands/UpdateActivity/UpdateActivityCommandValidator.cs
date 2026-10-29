using Application.Common.Interfaces;
using Application.Common.Interfaces.Queries;
using Application.Common.Validation;
using Domain.Activities;
using Domain.Categories;
using FluentValidation;

namespace Application.Activities.Commands.UpdateActivity;

public class UpdateActivityCommandValidator : AbstractValidator<UpdateActivityCommand>
{
    public UpdateActivityCommandValidator(ICategoryQueries categoryQueries, IDateTimeProvider dateTimeProvider)
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Title).MaximumLength(FieldLimits.Name).When(x => !string.IsNullOrWhiteSpace(x.Title));
        RuleFor(x => x.CustomTaskText).MaximumLength(FieldLimits.Name).When(x => !string.IsNullOrWhiteSpace(x.CustomTaskText));

        // Capped on drafts too, like the names above: it is a column limit, not a completion rule (BLUM-7673).
        RuleFor(x => x.ClosureReason)
            .MaximumLength(FieldLimits.ClosureReason)
            .When(x => !string.IsNullOrWhiteSpace(x.ClosureReason))
            .WithMessage(ActivityClosureReason.TooLongError);

        RuleFor(x => x.Status)
            .Must(ActivityStatus.IsSupported)
            .When(x => !string.IsNullOrWhiteSpace(x.Status))
            .WithMessage("Select a valid status.");

        When(x => !x.SaveAsDraft, () =>
        {
            RuleFor(x => x.CategoryId).NotNull().WithMessage("Select a category.");
            RuleFor(x => x.TaskId).NotNull().WithMessage("Select a type of work.");
            RuleFor(x => x.Status).NotEmpty().WithMessage("Select the stage the activity is at.");

            RuleFor(x => x.ClosureReason)
                .NotEmpty()
                .When(x => string.Equals(x.Status, ActivityStatuses.Closed, StringComparison.OrdinalIgnoreCase))
                .WithMessage(ActivityClosureReason.RequiredError);

            When(x => string.Equals(x.Status, ActivityStatuses.Active, StringComparison.OrdinalIgnoreCase), () =>
            {
                // One message per trigger, worded exactly as the 13 August Question Protocol lists them and
                // mirroring validateStartDate on the client (BLUM-7620). "A real date" covers both an impossible
                // month and a year outside the static bounds.
                // A Joint partnership is always Active and its record asks for no start date (BLUM-8090), so it is
                // the one Active activity allowed to complete without one. The range and future-date rules below
                // still apply to a date it does carry (a record created before that change).
                WhenAsync(
                    async (command, cancellationToken) => !await IsJointPartnershipAsync(categoryQueries, command.CategoryId, cancellationToken),
                    () =>
                    {
                        RuleFor(x => x.StartMonth).NotNull().WithMessage("The start date must include a month");
                        RuleFor(x => x.StartYear).NotNull().WithMessage("The start date must include a year");
                    });
                RuleFor(x => x.StartMonth).InclusiveBetween(1, 12).When(x => x.StartMonth is not null).WithMessage("The start date must be a real date");
                RuleFor(x => x.StartYear).InclusiveBetween(2000, 2100).When(x => x.StartYear is not null).WithMessage("The start date must be a real date");

                // The start date must not be in the future — same rule as create (edit validation mirrors
                // create). Guarded on the presence/range rules above so this message never stacks on top of
                // "enter a valid month/year". No trailing full stop, to match the client-side copy.
                RuleFor(x => x.StartMonth)
                    .Must((command, _) => !ActivityStartDate.IsInFuture(command.StartYear, command.StartMonth, dateTimeProvider.GetUtcNow()))
                    .When(x => x.StartMonth is >= 1 and <= 12 && x.StartYear is >= 2000 and <= 2100)
                    .WithMessage("The start date must be today or in the past");
            });
        });
    }

    private static async Task<bool> IsJointPartnershipAsync(ICategoryQueries categoryQueries, Guid? categoryId, CancellationToken cancellationToken) =>
        categoryId is { } id
        && await ExpectedResultInputs.ResolveCategoryNameAsync(categoryQueries, new CategoryId(id), cancellationToken) == CategoryNames.JointPartnership;
}