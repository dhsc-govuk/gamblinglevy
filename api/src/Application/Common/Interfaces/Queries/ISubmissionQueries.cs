using Domain.Activities;
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

    // Whether any local authority has started a report in the collection (a submission row exists). The collection
    // edits that must not change a started report's questions (BLUM-9084 / 9104) ask this, not for the rows.
    Task<bool> AnyForCollectionAsync(CollectionId collectionId, CancellationToken cancellationToken = default);

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

    // The answers this organisation SUBMITTED for one activity in an earlier reporting period, most recent period
    // first so the caller can carry forward the answer set of the first submission it sees (BLUM-7671).
    // Finalised submissions only: the acceptance criteria say "previously submitted data", and an organisation can
    // have two quarterly windows open at once (BLUM-6950), so the other window's in-flight draft is not a prior
    // report. Ordering is by the collection's reporting period, not by SubmittedAt — a late-filed Q2 must not
    // outrank Q3. AsNoTracking.
    Task<IReadOnlyList<PriorActivityAnswerRow>> GetPriorSubmittedActivityAnswersAsync(
        OrganisationId organisationId,
        ActivityId activityId,
        DateOnly beforeReportingPeriodEnd,
        CancellationToken cancellationToken = default);

    // Every answer this organisation SUBMITTED for ANY activity in a reporting period ending on or before the given
    // date — the maturity-assessment pre-fill's quarterly source (BLUM-7856), which needs the whole estate in one
    // read rather than the per-activity query above. Ordered per activity most-recent-period-first (then latest
    // SubmittedAt) so the caller can keep one report's answer set per activity. Finalised submissions only;
    // soft-deleted collections excluded. AsNoTracking.
    Task<IReadOnlyList<OrganisationActivityAnswerRow>> GetLatestSubmittedActivityAnswersForOrganisationAsync(
        OrganisationId organisationId,
        DateOnly onOrBeforeReportingPeriodEnd,
        CancellationToken cancellationToken = default);

    // Every answer this organisation SUBMITTED for one activity, across all its finalised quarterly reports, for the
    // activity-record export (BLUM-7648). Only reports in which that activity's results were Completed: answer rows
    // can also exist for an activity that was not reportable at submit time (carried forward on confirm, or answered
    // and then moved to Planned), and those were never part of the submitted report. Soft-deleted collections are
    // excluded. Ordered oldest reporting period first, then SubmittedAt. AsNoTracking.
    Task<IReadOnlyList<SubmittedActivityAnswerRow>> GetSubmittedAnswersForActivityAsync(
        OrganisationId organisationId,
        ActivityId activityId,
        CancellationToken cancellationToken = default);

    // Every spend entry this organisation SUBMITTED against one activity, across all its finalised quarterly reports
    // (BLUM-7648). Each row is that report's own entry — its actual for its own quarter, or a forecast it made for a
    // later quarter — so callers must never sum rows across reports: a later report's year-to-date figures already
    // restate earlier quarters. Same ordering and exclusions as the answers query above. AsNoTracking.
    Task<IReadOnlyList<SubmittedActivitySpendRow>> GetSubmittedSpendForActivityAsync(
        OrganisationId organisationId,
        ActivityId activityId,
        CancellationToken cancellationToken = default);
}

// The finalised report a submitted answer or spend entry belongs to, with enough of its collection to label it.
public readonly record struct SubmittedReportRef(
    Guid SubmissionId,
    string CollectionName,
    string? FinancialYear,
    int? Quarter,
    DateOnly? ReportingPeriodEnd,
    DateTime SubmittedAt,
    string? ReferenceNumber);

public readonly record struct SubmittedActivityAnswerRow(SubmittedReportRef Report, string QuestionCode, string Value);

// Quarter is null for an actual (it is the report's own quarter) and the forecast target quarter otherwise.
public readonly record struct SubmittedActivitySpendRow(SubmittedReportRef Report, bool IsForecast, int? Quarter, decimal Amount);

// One submitted quarterly-report answer of one activity, with enough of its provenance for the caller to pick the
// most recent report per activity.
public readonly record struct OrganisationActivityAnswerRow(
    Guid ActivityId,
    Guid SubmissionId,
    DateOnly ReportingPeriodEnd,
    DateTime SubmittedAt,
    string QuestionCode,
    string Value);

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

// One answer an organisation submitted for an activity in an earlier report, carried forward into the current
// one. Rows arrive most-recent-period-first. SubmissionId identifies which earlier report the row came from, so
// the caller can take one submission's answer set whole rather than filling gaps per code from older reports —
// a blank is never stored, so "absent" and "deliberately cleared" are otherwise indistinguishable.
public readonly record struct PriorActivityAnswerRow(Guid SubmissionId, string QuestionCode, string Value);