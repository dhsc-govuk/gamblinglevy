using Application.Common.Permissions;
using LanguageExt;

namespace Api.Services.Abstract;

public interface IMaturityAssessmentExportService
{
    // The LA's own "Download your assessment" ZIP for a SUBMITTED assessment. None until submitted.
    Task<Option<ReportExportFile>> GenerateAsync(Permission permission, Guid collectionId, CancellationToken cancellationToken);

    // DHSC variant: the same per-organisation ZIP for an explicitly named organisation.
    Task<Option<ReportExportFile>> GenerateForOrganisationAsync(Guid collectionId, Guid organisationId, CancellationToken cancellationToken);
}