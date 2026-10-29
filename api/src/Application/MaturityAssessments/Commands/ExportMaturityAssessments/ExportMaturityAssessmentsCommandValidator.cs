using Domain.Organisations;
using FluentValidation;

namespace Application.MaturityAssessments.Commands.ExportMaturityAssessments;

public class ExportMaturityAssessmentsCommandValidator : AbstractValidator<ExportMaturityAssessmentsCommand>
{
    public ExportMaturityAssessmentsCommandValidator()
    {
        RuleFor(x => x.CollectionId).NotEmpty();

        // The same wording as ExportSubmissionsCommandValidator: the client string-matches "supported English
        // regions" to pick its GDS copy — one phrasing for one rule.
        RuleFor(x => x.Region)
            .Must(Region.IsSupported)
            .WithMessage("Region must be one of the supported English regions.")
            .When(x => x.Region is not null);
    }
}