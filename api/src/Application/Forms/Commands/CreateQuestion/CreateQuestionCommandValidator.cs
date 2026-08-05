using Application.Common.Validation;
using Domain.Forms;
using FluentValidation;

namespace Application.Forms.Commands.CreateQuestion;

public class CreateQuestionCommandValidator : AbstractValidator<CreateQuestionCommand>
{
    public CreateQuestionCommandValidator()
    {
        RuleFor(x => x.SectionId).NotEmpty();
        RuleFor(x => x.Code).NotEmpty().SafeCode(FieldLimits.Code);
        RuleFor(x => x.Label).NotEmpty().MaximumLength(FieldLimits.Label);
        RuleFor(x => x.QuestionType)
            .NotEmpty()
            .Must(QuestionType.IsSupported).WithMessage("Select a valid question type.");
        RuleFor(x => x.Hint).MaximumLength(FieldLimits.Hint).When(x => !string.IsNullOrWhiteSpace(x.Hint));
        RuleFor(x => x.ConditionalValue).MaximumLength(FieldLimits.ShortText).When(x => !string.IsNullOrWhiteSpace(x.ConditionalValue));

        // All previously unbounded, and the first three have a column narrower than the request allowed:
        // help_text_summary is varchar(500), conditional_question_code varchar(100), and the option value
        // and label varchar(255)/varchar(500). Each was a 22001 on SaveChanges, surfacing as a 500
        // (BLUM-7240).
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