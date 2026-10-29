using Application.Common.Validation;
using FluentValidation;

namespace Application.MaturityAssessments.Commands.CompleteMaturitySection;

public class CompleteMaturitySectionCommandValidator : AbstractValidator<CompleteMaturitySectionCommand>
{
    public CompleteMaturitySectionCommandValidator()
    {
        RuleFor(x => x.CollectionId).NotEmpty();
        RuleFor(x => x.SectionCode).NotEmpty().MaximumLength(FieldLimits.Code);
    }
}