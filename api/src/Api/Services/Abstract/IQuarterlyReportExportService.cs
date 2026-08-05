using Application.Common.Permissions;
using LanguageExt;

namespace Api.Services.Abstract;

public interface IQuarterlyReportExportService
{
    // Builds the downloadable ZIP (one CSV per section + a summary) for a SUBMITTED quarterly report. None when
    // there is no eligible, submitted report for the caller's organisation against this collection — the export is
    // only ever the immutable, finalised copy "for your records".
    Task<Option<ReportExportFile>> GenerateAsync(Permission permission, Guid collectionId, CancellationToken cancellationToken);

    // DHSC variant (BLUM-6100): the same per-organisation report ZIP, but for an explicitly named organisation rather
    // than the caller's own. Used by the admin submission-detail screen. None when the org is not assigned or has no
    // submitted report. Uses the frozen snapshot only — the live render fallback is permission-scoped and unavailable
    // to admins, so legacy submissions with no snapshot export an empty document.
    Task<Option<ReportExportFile>> GenerateForOrganisationAsync(Guid collectionId, Guid organisationId, CancellationToken cancellationToken);
}

public record ReportExportFile(string FileName, byte[] Content);