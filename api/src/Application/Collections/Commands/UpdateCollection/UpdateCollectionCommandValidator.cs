using Application.Common.Validation;
using Domain.Collections;
using FluentValidation;

namespace Application.Collections.Commands.UpdateCollection;

public class UpdateCollectionCommandValidator : AbstractValidator<UpdateCollectionCommand>
{
    public UpdateCollectionCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("A collection id is required.");

        // Only the name is always required — a draft ("Save and come back later") may leave every other setup
        // question unanswered. Values that ARE supplied are validated regardless of draft state.
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Enter a name for the collection.")
            .MaximumLength(FieldLimits.Name).WithMessage($"The name must be {FieldLimits.Name} characters or fewer.");

        RuleFor(x => x.Purpose)
            .MaximumLength(FieldLimits.Purpose).WithMessage($"The purpose must be {FieldLimits.Purpose} characters or fewer.")
            .When(x => !string.IsNullOrWhiteSpace(x.Purpose));

        RuleFor(x => x.Type)
            .Must(CollectionType.IsSupported)
            .When(x => !string.IsNullOrWhiteSpace(x.Type))
            .WithMessage("Select a valid collection type.");

        // collections.financial_year is varchar(16). This rule said 20, so a 17-to-20 character value passed
        // validation and then raised 22001 on SaveChanges as a 500 (BLUM-7240).
        RuleFor(x => x.FinancialYear)
            .MaximumLength(FieldLimits.FinancialYear)
            .WithMessage($"The financial year must be {FieldLimits.FinancialYear} characters or fewer.");

        // A quarter is only meaningful for a quarterly report; when supplied it must be 1-4, and a maturity
        // assessment must not carry one (it covers the whole financial year).
        RuleFor(x => x.Quarter)
            .InclusiveBetween(1, 4)
            .When(x => x.Quarter.HasValue)
            .WithMessage("The quarter must be between 1 and 4.");

        RuleFor(x => x.Quarter)
            .Null()
            .When(x => string.Equals(x.Type, CollectionTypes.MaturityAssessment, StringComparison.Ordinal))
            .WithMessage("A maturity assessment must not have a quarter.");

        RuleFor(x => x.Reminders).Bounded(FieldLimits.MaxReminders);

        RuleForEach(x => x.Reminders).ChildRules(reminder =>
        {
            reminder.RuleFor(r => r.OffsetType)
                .Must(ReminderOffsetType.IsSupported)
                .WithMessage("Select a valid reminder offset.");
            reminder.RuleFor(r => r.Days)
                .InclusiveBetween(0, FieldLimits.MaxReminderDays)
                .WithMessage($"A reminder must be between 0 and {FieldLimits.MaxReminderDays} days.");
        });

        // Dates that are both supplied must be ordered, draft or not.
        RuleFor(x => x.SubmissionDeadline)
            .GreaterThanOrEqualTo(x => x.CollectionOpenDate!.Value)
            .When(x => x.CollectionOpenDate.HasValue && x.SubmissionDeadline.HasValue)
            .WithMessage("The submission deadline must be on or after the collection open date.");

        // Completeness is only enforced when finishing setup (publishing), not when saving a draft.
        When(x => !x.SaveAsDraft, () =>
        {
            RuleFor(x => x.Type)
                .NotEmpty().WithMessage("Select a collection type.");

            RuleFor(x => x.FinancialYear)
                .NotEmpty().WithMessage("Enter the financial year.");

            RuleFor(x => x.WindowSameAsReportingPeriod)
                .NotNull().WithMessage("Select whether the collection window is the same as the reporting period.");

            // Explicit window dates are only required when the window differs from the reporting period.
            When(x => x.WindowSameAsReportingPeriod == false, () =>
            {
                RuleFor(x => x.CollectionOpenDate).NotNull().WithMessage("Enter the collection open date.");
                RuleFor(x => x.SubmissionDeadline).NotNull().WithMessage("Enter the submission deadline.");
            });

            RuleFor(x => x.Quarter)
                .NotNull()
                .When(x => string.Equals(x.Type, CollectionTypes.QuarterlyReport, StringComparison.Ordinal))
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