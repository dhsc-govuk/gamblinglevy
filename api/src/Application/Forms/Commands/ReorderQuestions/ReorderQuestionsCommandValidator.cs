using Application.Common.Validation;
using FluentValidation;

namespace Application.Forms.Commands.ReorderQuestions;

public class ReorderQuestionsCommandValidator : AbstractValidator<ReorderQuestionsCommand>
{
    public ReorderQuestionsCommandValidator()
    {
        RuleFor(x => x.SectionId).NotEmpty();

        // Display order is assigned from the list index, so a repeated id does not fail — it silently
        // orders the section wrongly (BLUM-7240).
        RuleFor(x => x.OrderedIds)
            .NotEmpty()
            .Bounded(FieldLimits.MaxCollectionItems)
            .DistinctIds();
    }
}