using Application.Common.Validation;
using FluentValidation;

namespace Application.DataCollectionForms.Commands.ReorderFormQuestions;

public class ReorderFormQuestionsCommandValidator : AbstractValidator<ReorderFormQuestionsCommand>
{
    public ReorderFormQuestionsCommandValidator()
    {
        RuleFor(x => x.FormModuleId).NotEmpty();

        // DistinctIds is the rule that matters here: display order is assigned from the list index, so a
        // repeated id does not fail, it silently orders the module wrongly.
        RuleFor(x => x.OrderedQuestionIds)
            .NotEmpty()
            .Bounded(FieldLimits.MaxCollectionItems)
            .DistinctIds();

        RuleForEach(x => x.OrderedQuestionIds).NotEmpty();
    }
}