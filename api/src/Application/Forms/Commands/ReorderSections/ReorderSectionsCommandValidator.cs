using Application.Common.Validation;
using FluentValidation;

namespace Application.Forms.Commands.ReorderSections;

public class ReorderSectionsCommandValidator : AbstractValidator<ReorderSectionsCommand>
{
    public ReorderSectionsCommandValidator()
    {
        // See ReorderQuestionsCommandValidator: a duplicate id corrupts display order silently rather than
        // failing (BLUM-7240).
        RuleFor(x => x.OrderedIds)
            .NotEmpty()
            .Bounded(FieldLimits.MaxCollectionItems)
            .DistinctIds();
    }
}