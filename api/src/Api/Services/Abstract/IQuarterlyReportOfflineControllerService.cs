using Api.Dtos;
using Application.Submissions.Exceptions;
using LanguageExt;

namespace Api.Services.Abstract;

// Read side of "Complete your report offline" (BLUM-8310): a section's CSV template.
public interface IQuarterlyReportOfflineControllerService
{
    Task<Either<SubmissionException, OfflineTemplateFileDto>> GetTemplateAsync(
        Guid collectionId,
        string moduleCode,
        CancellationToken cancellationToken);
}