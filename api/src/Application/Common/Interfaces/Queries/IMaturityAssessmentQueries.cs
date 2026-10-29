using Domain.Collections;
using Domain.MaturityAssessments;
using Domain.Organisations;
using LanguageExt;

namespace Application.Common.Interfaces.Queries;

public interface IMaturityAssessmentQueries
{
    // Tracked load including Sections — command handlers mutate the aggregate and rely on the change tracker to diff
    // the owned section rows on save (the Submission precedent; an untracked load makes section changes a no-op).
    Task<Option<MaturityAssessment>> GetByCollectionAndOrgAsync(CollectionId collectionId, OrganisationId organisationId, CancellationToken cancellationToken = default);

    // The DHSC status roll-up's projection: row presence means started, SubmittedAt means finalised. AsNoTracking.
    Task<IReadOnlyList<MaturityAssessmentStatusRow>> GetStatusRowsForCollectionAsync(CollectionId collectionId, CancellationToken cancellationToken = default);

    // One row per collection this organisation has started an assessment for (the LA history list). AsNoTracking.
    Task<IReadOnlyList<OrganisationMaturityAssessmentRow>> GetForOrganisationAsync(OrganisationId organisationId, CancellationToken cancellationToken = default);

    // The organisation's most recent SUBMITTED assessment for an EARLIER financial year — the "prior MA" pre-fill
    // source (BLUM-7856 AC3). Ordered by the collection's reporting-period end, not SubmittedAt, so a late-filed
    // earlier year never outranks a later one; soft-deleted collections excluded. AsNoTracking.
    Task<Option<PriorMaturityAssessmentRow>> GetLatestSubmittedBeforeAsync(OrganisationId organisationId, DateOnly beforeReportingPeriodEnd, CancellationToken cancellationToken = default);

    // Lightweight detail for the DHSC / LA read-only screens (sections + frozen snapshot). AsNoTracking.
    Task<Option<MaturityAssessmentDetailRow>> GetDetailForCollectionAndOrgAsync(CollectionId collectionId, OrganisationId organisationId, CancellationToken cancellationToken = default);

    // Every SUBMITTED assessment in a collection with its frozen snapshot — the consolidated DHSC export's input.
    // Submitted rows only: a draft has no snapshot and nothing to export. AsNoTracking.
    Task<IReadOnlyList<MaturityAssessmentExportRow>> GetSubmittedForCollectionAsync(CollectionId collectionId, CancellationToken cancellationToken = default);

    // Finalised assessments across every collection sharing a financial year — the reference-number sequence
    // granularity (next sequence = count + 1), mirroring ISubmissionQueries.CountSubmittedForPeriodAsync.
    Task<int> CountSubmittedForFinancialYearAsync(string financialYear, CancellationToken cancellationToken = default);

    // Whether the organisation has ever started an assessment — the delete-organisation guard's question.
    Task<bool> HasAnyForOrganisationAsync(OrganisationId organisationId, CancellationToken cancellationToken = default);
}

public readonly record struct MaturityAssessmentStatusRow(Guid OrganisationId, DateTime? SubmittedAt);

public readonly record struct OrganisationMaturityAssessmentRow(Guid CollectionId, DateTime? SubmittedAt);

public readonly record struct PriorMaturityAssessmentRow(Guid AssessmentId, Guid CollectionId, string? FinancialYear, DateOnly ReportingPeriodEnd, DateTime SubmittedAt);

public readonly record struct MaturityAssessmentSectionRow(string SectionCode, string Status);

public readonly record struct MaturityAssessmentDetailRow(
    Guid AssessmentId,
    DateTime? SubmittedAt,
    Guid? SubmittedBy,
    string? ReferenceNumber,
    string? SnapshotJson,
    IReadOnlyList<MaturityAssessmentSectionRow> Sections);

public readonly record struct MaturityAssessmentExportRow(
    Guid OrganisationId,
    DateTime SubmittedAt,
    Guid? SubmittedBy,
    string? ReferenceNumber,
    string? SnapshotJson);