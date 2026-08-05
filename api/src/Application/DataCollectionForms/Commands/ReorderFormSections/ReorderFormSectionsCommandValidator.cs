using Application.Common.Validation;
using FluentValidation;

namespace Application.DataCollectionForms.Commands.ReorderFormSections;

public class ReorderFormSectionsCommandValidator : AbstractValidator<ReorderFormSectionsCommand>
{
    public ReorderFormSectionsCommandValidator()
    {
        RuleFor(x => x.FormModuleId).NotEmpty();

        // See ReorderQuestionsCommandValidator: a duplicate id corrupts display order silently rather than
        // failing (BLUM-7240).
        RuleFor(x => x.OrderedSectionIds)
            .NotEmpty()
            .Bounded(FieldLimits.MaxCollectionItems)
            .DistinctIds();

        RuleForEach(x => x.OrderedSectionIds).NotEmpty();
    }
}