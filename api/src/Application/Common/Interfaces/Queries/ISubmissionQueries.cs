using Domain.Collections;
using Domain.Organisations;
using Domain.Submissions;
using LanguageExt;

namespace Application.Common.Interfaces.Queries;

public interface ISubmissionQueries
{
    // Tracked load including Sections — command handlers mutate the aggregate and rely on the change tracker to
    // diff the owned section rows on save (the Collection precedent).
    Task<Option<Submission>> GetByCollectionAndOrgAsync(CollectionId collectionId, OrganisationId organisationId, CancellationToken cancellationToken = default);

    // The organisation's submissions whose collection falls in the given financial year, with each collection's
    // quarter. Feeds the "spend to date" roll-up (sum of actual spend in earlier quarters of the same year) and the
    // funding dashboard's cumulative spend.
    Task<IReadOnlyList<(SubmissionId SubmissionId, int? Quarter)>> GetForOrgAndFinancialYearAsync(OrganisationId organisationId, string financialYear, CancellationToken cancellationToken = default);

    // The count of submissions already finalised (SubmittedAt set) across every collection sharing a financial year +
    // quarter — the same granularity as the reference-number prefix. Drives the reference sequence on submit (next
    // sequence = count + 1), so two collections in the same FY+quarter cannot both mint sequence 1 and collide.
    Task<int> CountSubmittedForPeriodAsync(string financialYear, int quarter, CancellationToken cancellationToken = default);

    // Every submission row for a collection, carrying the frozen snapshot — the cross-organisation CSV export reads
    // its section content from it. AsNoTracking. Deliberately NOT used by the status list: ReportSnapshotJson is a
    // whole rendered report per organisation, and a window can hold ~160 of them, so the status screen uses the lean
    // projection below instead.
    Task<IReadOnlyList<CollectionSubmissionRow>> GetSubmissionsForCollectionAsync(CollectionId collectionId, CancellationToken cancellationToken = default);

    // The two columns the DHSC submission-status roll-up actually needs. The presence of a row for an organisation
    // means it has started; SubmittedAt set means it has finalised. The caller resolves which organisations are
    // assigned (incl. the "submitted by all LAs" implicit case) and emits Not started rows for assigned orgs with no
    // row here.
    Task<IReadOnlyList<CollectionSubmissionStatusRow>> GetStatusRowsForCollectionAsync(CollectionId collectionId, CancellationToken cancellationToken = default);

    // One organisation's submission against a collection, projected for the DHSC submission-detail screen.
    // None when the organisation has not started a submission for this collection (a graceful empty state).
    Task<Option<SubmissionDetailRow>> GetDetailForCollectionAndOrgAsync(CollectionId collectionId, OrganisationId organisationId, CancellationToken cancellationToken = default);

    // The (collection, finalised-at) pair for every submission the organisation has started, for the LA-facing "my
    // submission history" list (BLUM-6097). AsNoTracking — read-only. One row per collection the org has at least
    // started; the caller joins these against the org's collections to derive each window's per-org status (presence
    // of a row means started; SubmittedAt set means finalised).
    Task<IReadOnlyList<OrganisationSubmissionRow>> GetForOrganisationAsync(OrganisationId organisationId, CancellationToken cancellationToken = default);
}

// Read projection of a submission for the cross-organisation CSV export. SubmittedAt/ReferenceNumber/ReportSnapshotJson
// are null until the report is finalised; ReportSnapshotJson is the frozen report the section files are built from.
public readonly record struct CollectionSubmissionRow(Guid OrganisationId, DateTime? SubmittedAt, string? ReferenceNumber, string? ReportSnapshotJson);

// The DHSC org-status list's projection: row presence means started, SubmittedAt means finalised. Nothing else is
// read, so nothing else is selected.
public readonly record struct CollectionSubmissionStatusRow(Guid OrganisationId, DateTime? SubmittedAt);

// Lightweight read projection of a single submission for the DHSC detail screen. SubmittedBy is the finalising user
// (resolved to a name/email by the controller service); null until the report is submitted.
public readonly record struct SubmissionDetailRow(Guid SubmissionId, DateTime? SubmittedAt, Guid? SubmittedBy, string? ReferenceNumber);

// Lightweight read projection of one organisation's submission against a collection, for the LA submission-history
// list. CollectionId keys the row against the org's collections; SubmittedAt is null until the report is finalised.
public readonly record struct OrganisationSubmissionRow(Guid CollectionId, DateTime? SubmittedAt);