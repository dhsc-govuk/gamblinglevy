using Application.Common.Validation;
using FluentValidation;

namespace Application.MaturityAssessments.Commands.SaveMaturityAnswers;

public class SaveMaturityAnswersCommandValidator : AbstractValidator<SaveMaturityAnswersCommand>
{
    public SaveMaturityAnswersCommandValidator()
    {
        RuleFor(x => x.CollectionId).NotEmpty();
        RuleFor(x => x.SectionCode).NotEmpty().MaximumLength(FieldLimits.Code);
        RuleFor(x => x.PageCode).NotEmpty().MaximumLength(FieldLimits.Code);

        // Bounded like every other list body (BLUM-7240): the largest page is ~25 questions, so 500 is generous.
        RuleFor(x => x.Answers).NotNull().Must(a => a.Count <= FieldLimits.MaxCollectionItems)
            .WithMessage($"No more than {FieldLimits.MaxCollectionItems} answers may be submitted at once.");
        RuleForEach(x => x.Answers).ChildRules(answer =>
        {
            answer.RuleFor(a => a.QuestionCode).NotEmpty().MaximumLength(FieldLimits.Code);
            answer.RuleFor(a => a.Value).MaximumLength(FieldLimits.AnswerText);
        });
    }
}