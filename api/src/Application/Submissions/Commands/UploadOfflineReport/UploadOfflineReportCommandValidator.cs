using FluentValidation;

namespace Application.Submissions.Commands.UploadOfflineReport;

// Only the route id. Everything about the file is checked by the handler after the report's write gate, so a caller
// who may not write to the report is refused for that, not for their file.
public class UploadOfflineReportCommandValidator : AbstractValidator<UploadOfflineReportCommand>
{
    public UploadOfflineReportCommandValidator()
    {
        RuleFor(x => x.CollectionId).NotEmpty();
    }
}