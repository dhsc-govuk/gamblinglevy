using Application.Common.Validation;
using Domain.Forms;
using FluentValidation;

namespace Application.DataCollectionForms.Commands.UpdateFormQuestion;

public class UpdateFormQuestionCommandValidator : AbstractValidator<UpdateFormQuestionCommand>
{
    public UpdateFormQuestionCommandValidator()
    {
        RuleFor(x => x.FormModuleId).NotEmpty();
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Label).NotEmpty().MaximumLength(FieldLimits.Label);
        RuleFor(x => x.QuestionType)
            .NotEmpty()
            .Must(QuestionType.IsSupported).WithMessage("Select a valid question type.");
        RuleFor(x => x.Hint).MaximumLength(FieldLimits.Hint).When(x => !string.IsNullOrWhiteSpace(x.Hint));
        RuleFor(x => x.HelpText).MaximumLength(FieldLimits.LongText).When(x => !string.IsNullOrWhiteSpace(x.HelpText));

        RuleFor(x => x.Options)
            .NotEmpty().WithMessage("Add at least one option for this question type.")
            .When(x => QuestionType.IsChoiceName(x.QuestionType));

        // See CreateFormQuestionCommandValidator: both option columns were unbounded here (BLUM-7240).
        RuleFor(x => x.Options).Bounded(FieldLimits.MaxCollectionItems);
        RuleForEach(x => x.Options).ChildRules(option =>
        {
            option.RuleFor(o => o.Value).NotEmpty().MaximumLength(FieldLimits.OptionValue);
            option.RuleFor(o => o.Label).NotEmpty().MaximumLength(FieldLimits.Label);
        });
    }
}