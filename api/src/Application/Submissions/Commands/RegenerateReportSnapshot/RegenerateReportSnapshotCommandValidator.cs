using FluentValidation;

namespace Application.Submissions.Commands.RegenerateReportSnapshot;

public class RegenerateReportSnapshotCommandValidator : AbstractValidator<RegenerateReportSnapshotCommand>
{
    public RegenerateReportSnapshotCommandValidator()
    {
        RuleFor(x => x.CollectionId).NotEmpty();
        RuleFor(x => x.OrganisationId).NotEmpty();
    }
}