using Application.Common.Validation;
using Domain.Collections;
using FluentValidation;

namespace Application.Collections.Commands.CreateCollection;

public class CreateCollectionCommandValidator : AbstractValidator<CreateCollectionCommand>
{
    public CreateCollectionCommandValidator()
    {
        // Only the name is always required — a draft ("Save and come back later") may leave every other setup
        // question unanswered. Values that ARE supplied are validated regardless of draft state.
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Enter a name for the collection.")
            .MaximumLength(FieldLimits.Name);

        RuleFor(x => x.Purpose)
            .MaximumLength(FieldLimits.Purpose).WithMessage($"The purpose must be {FieldLimits.Purpose} characters or fewer.")
            .When(x => !string.IsNullOrWhiteSpace(x.Purpose));

        // collections.financial_year is varchar(16), so an unbounded value passed validation here and then
        // raised 22001 on SaveChanges as a 500 (BLUM-7240).
        RuleFor(x => x.FinancialYear)
            .MaximumLength(FieldLimits.FinancialYear)
            .WithMessage($"The financial year must be {FieldLimits.FinancialYear} characters or fewer.");

        RuleFor(x => x.Type)
            .Must(CollectionType.IsSupported)
            .When(x => !string.IsNullOrWhiteSpace(x.Type))
            .WithMessage("Select a valid collection type.");

        // A quarter is only meaningful when supplied — when present it must be 1-4 regardless of draft state,
        // and a maturity assessment must not carry one (it covers the whole financial year).
        RuleFor(x => x.Quarter)
            .InclusiveBetween(1, 4)
            .When(x => x.Quarter.HasValue)
            .WithMessage("Select a quarter between 1 and 4.");

        RuleFor(x => x.Quarter)
            .Null()
            .When(x => x.Type == CollectionTypes.MaturityAssessment)
            .WithMessage("A maturity assessment must not have a quarter.");

        RuleFor(x => x.OpenDate)
            .Must((command, openDate) => openDate!.Value <= command.Deadline!.Value)
            .When(x => x.OpenDate.HasValue && x.Deadline.HasValue)
            .WithMessage("The collection open date must be on or before the submission deadline.");

        RuleFor(x => x.Reminders).Bounded(FieldLimits.MaxReminders);

        // Reminders (when supplied) must use a valid offset and a non-negative day count — '0' means the first
        // day after opening / the last day before the deadline. The upper bound exists because the day count is
        // fed to AddDays when the reminder is scheduled.
        RuleForEach(x => x.Reminders).ChildRules(reminder =>
        {
            reminder.RuleFor(r => r.OffsetType)
                .Must(ReminderOffsetType.IsSupported)
                .WithMessage("Select a valid reminder offset.");
            reminder.RuleFor(r => r.Days)
                .InclusiveBetween(0, FieldLimits.MaxReminderDays)
                .WithMessage($"A reminder must be between 0 and {FieldLimits.MaxReminderDays} days.");
        });

        // Completeness is only enforced when finishing setup (publishing), not when saving a draft.
        When(x => !x.SaveAsDraft, () =>
        {
            RuleFor(x => x.Type)
                .NotEmpty().WithMessage("Select a collection type.");

            RuleFor(x => x.FinancialYear)
                .NotEmpty().WithMessage("Enter a financial year.");

            RuleFor(x => x.WindowSameAsReportingPeriod)
                .NotNull().WithMessage("Select whether the collection window is the same as the reporting period.");

            When(x => x.WindowSameAsReportingPeriod == false, () =>
            {
                RuleFor(x => x.OpenDate)
                    .NotNull().WithMessage("Enter a collection open date.");

                RuleFor(x => x.Deadline)
                    .NotNull().WithMessage("Enter a submission deadline.");
            });

            RuleFor(x => x.Quarter)
                .NotNull()
                .When(x => x.Type == CollectionTypes.QuarterlyReport)
                .WithMessage("Select a quarter for a quarterly report.");

            RuleFor(x => x.IsSubmittedByAllLas)
                .NotNull().WithMessage("Select whether the data collection is submitted by all local authorities.");

            When(x => x.IsSubmittedByAllLas == false, () =>
            {
                RuleFor(x => x.LocalAuthorityIds)
                    .NotEmpty().WithMessage("Select at least one Local Authority.");
            });

            RuleFor(x => x.FormModuleIds)
                .NotEmpty().WithMessage("Select at least one form module.");
        });
    }
}