using Application.Common.Validation;
using Domain.Forms;
using FluentValidation;

namespace Application.Forms.Commands.UpdateQuestion;

public class UpdateQuestionCommandValidator : AbstractValidator<UpdateQuestionCommand>
{
    public UpdateQuestionCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.SectionId).NotEmpty();
        RuleFor(x => x.Label).NotEmpty().MaximumLength(FieldLimits.Label);
        RuleFor(x => x.QuestionType)
            .NotEmpty()
            .Must(QuestionType.IsSupported).WithMessage("Select a valid question type.");
        RuleFor(x => x.Hint).MaximumLength(FieldLimits.Hint).When(x => !string.IsNullOrWhiteSpace(x.Hint));
        RuleFor(x => x.ConditionalValue).MaximumLength(FieldLimits.ShortText).When(x => !string.IsNullOrWhiteSpace(x.ConditionalValue));

        // See CreateQuestionCommandValidator: all previously unbounded, and each of the first three narrower
        // in the schema than the request allowed (BLUM-7240).
        RuleFor(x => x.HelpTextSummary).MaximumLength(FieldLimits.HelpTextSummary).When(x => !string.IsNullOrWhiteSpace(x.HelpTextSummary));
        RuleFor(x => x.ConditionalQuestionCode).MaximumLength(FieldLimits.Code).When(x => !string.IsNullOrWhiteSpace(x.ConditionalQuestionCode));
        RuleFor(x => x.HelpText).MaximumLength(FieldLimits.LongText).When(x => !string.IsNullOrWhiteSpace(x.HelpText));

        RuleFor(x => x.Options).Bounded(FieldLimits.MaxCollectionItems);
        RuleForEach(x => x.Options).ChildRules(option =>
        {
            option.RuleFor(o => o.Value).NotEmpty().MaximumLength(FieldLimits.OptionValue);
            option.RuleFor(o => o.Label).NotEmpty().MaximumLength(FieldLimits.Label);
        });
    }
}