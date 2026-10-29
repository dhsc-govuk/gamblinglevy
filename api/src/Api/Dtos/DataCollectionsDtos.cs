using Domain.Collections;

namespace Api.Dtos;

// Transport shape for a single reminder on create/update requests: the offset type
// (after_open / before_deadline) and the number of days.
public record ReminderRequestDto(string OffsetType, int Days);

// Setup questions other than Name are nullable: a draft ("Save and come back later") may leave any of them
// unanswered, and null means exactly that — it must never be coerced to false/empty defaults.
public record CreateCollectionRequestDto(
    string Name,
    string? Purpose,
    string? Type,
    string? FinancialYear,
    int? Quarter,
    bool? WindowSameAsReportingPeriod,
    DateOnly? OpenDate,
    DateOnly? Deadline,
    bool? IsSubmittedByAllLas,
    bool SaveAsDraft,
    IReadOnlyList<Guid>? LocalAuthorityIds = null,
    IReadOnlyList<Guid>? FormModuleIds = null,
    IReadOnlyList<ReminderRequestDto>? Reminders = null,
    IReadOnlyList<CollectionFormVersionRequestDto>? FormVersions = null);

public record UpdateCollectionRequestDto(
    string? Name,
    string? Purpose,
    string? Type,
    string? FinancialYear,
    int? Quarter,
    bool? WindowSameAsReportingPeriod,
    DateOnly? CollectionOpenDate,
    DateOnly? SubmissionDeadline,
    bool? IsSubmittedByAllLas,
    bool SaveAsDraft,
    IReadOnlyList<Guid>? FormModuleIds = null,
    IReadOnlyList<Guid>? LocalAuthorityIds = null,
    IReadOnlyList<ReminderRequestDto>? Reminders = null,
    IReadOnlyList<CollectionFormVersionRequestDto>? FormVersions = null);

// BLUM-9103: FormVersions on the create and update requests is accepted and ignored, unvalidated. The PO rejected a
// per-collection version choice, so a collection always follows the latest version until its window opens. The client
// still sends it while BLUM_9087 is on, and the API refuses unknown JSON members (UnmappedMemberHandling.Disallow in
// SetupModule), so removing it would make the wizard's save a 400. Delete it, and this record, when the client part of
// that change stops sending it.
public record CollectionFormVersionRequestDto(Guid ModuleId, int? VersionNumber);

// VersionNumber is the version the collection records (null until it is first published, and for a maturity section).
// VersionChosen is always false since BLUM-9103 removed the version choice; the collection wizard still reads it, so
// it goes with the client part of that change.
public record CollectionFormDto(Guid ModuleId, string? Code, string? Name, int? VersionNumber = null, bool VersionChosen = false);

public record CollectionLocalAuthorityDto(Guid Id, string? Name);

public record CollectionReminderDto(string OffsetType, int Days);

public record CollectionDto(
    Guid Id,
    string Name,
    string? Purpose,
    string? Type,
    string? FinancialYear,
    int? Quarter,
    DateOnly? ReportingPeriodStart,
    DateOnly? ReportingPeriodEnd,
    bool? WindowSameAsReportingPeriod,
    DateOnly? OpensDate,
    DateOnly? Deadline,
    bool? IsSubmittedByAllLas,
    IReadOnlyList<CollectionFormDto> Forms,
    IReadOnlyList<CollectionLocalAuthorityDto> LocalAuthorities,
    IReadOnlyList<CollectionReminderDto> Reminders,
    string Status,
    bool IsDraft,
    DateTime CreatedAt,
    Guid? CreatedBy,
    string? CreatedByName,
    DateTime? UpdatedAt,
    Guid? UpdatedBy,
    string? UpdatedByName);

public record CollectionSummaryDto(
    Guid Id,
    string Name,
    string? Type,
    DateOnly? Deadline,
    string Status)
{
    public static CollectionSummaryDto FromDomainModel(Collection collection, DateTime utcNow) =>
        new(
            collection.Id.Value,
            collection.Name,
            collection.Type?.ToString(),
            collection.SubmissionDeadline,
            collection.GetStatus(utcNow).ToString());
}

// Type is the collection type the option belongs to (BLUM-7943): a quarterly report picks from the form modules, a
// maturity assessment from its catalogue sections, which have no last-changed date. One list carries both so the
// wizard can filter locally when the admin switches type.
// Versions (BLUM-9103) is newest first, and empty for a maturity section, which has none.
public record CollectionFormOptionDto(Guid ModuleId, string Name, DateTime? LastChangedAt, string Type, IReadOnlyList<CollectionFormVersionOptionDto>? Versions = null);

public record CollectionFormVersionOptionDto(int VersionNumber, DateTime PublishedAt);

public record ReportingLaDto(Guid Id, string Name);

// DHSC submission-visibility: the per-window organisation status list (BLUM-6095). One row per organisation assigned
// to the collection, with its derived submission status. CollectionStatus is the collection's own lifecycle status
// (Open/Closed/…).
//
// Carries the same two distinct date ranges as OrgSubmissionDetailDto below — StartDate/Deadline are the COLLECTION
// WINDOW, Quarter/ReportingPeriodStart/End are the PERIOD BEING REPORTED ON. The page needs both: it lists the
// window as "Start date"/"Deadline", and names the consolidated export by the reporting period (BLUM-7079).
public record CollectionSubmissionSummaryDto(
    Guid CollectionId,
    string CollectionName,
    string CollectionStatus,
    DateOnly? StartDate,
    DateOnly? Deadline,
    int? Quarter,
    DateOnly? ReportingPeriodStart,
    DateOnly? ReportingPeriodEnd,
    IReadOnlyList<OrgSubmissionStatusDto> Organisations,
    // Trailing, optional: the screen serves both submission flows (BLUM-7855 AC4). A maturity-assessment window has
    // no Quarter and is titled by its financial year instead.
    string? CollectionType = null,
    string? FinancialYear = null);

// Deliberately carries no export file size. Producing one meant building the organisation's whole report ZIP in
// memory — for every submitted organisation in the window, up to ~160 of them, on every page load — and the
// Organisations screen never read the field. The size belongs to the detail screen, which shows one organisation
// and renders it (OrgSubmissionDetailDto.ExportSizeBytes below).
public record OrgSubmissionStatusDto(
    Guid OrganisationId,
    string OrganisationName,
    string Status,
    DateTime? SubmittedAt);

// DHSC submission detail for one organisation against one collection (BLUM-6445). HasSubmission drives the empty
// state; SubmittedByName/Email/SubmittedAt/ReferenceNumber are populated only once the report is finalised.
//
// ExportSizeBytes captions the download ("ZIP, 389 KB"). It is measured by building this one organisation's ZIP
// through the call the download endpoint makes (cached briefly), and is null until the report is finalised or when the
// measurement failed, when the screen shows the format alone. ReportExportSizeService says why it is measured rather
// than stored (BLUM-9018, reversing BLUM-7216 for the detail screen only).
//
// TWO DISTINCT DATE RANGES, do not conflate them (BLUM-7079):
//   StartDate/Deadline               = the COLLECTION WINDOW — when LAs may submit (CollectionOpenDate/SubmissionDeadline,
//                                      admin-entered on the collection wizard).
//   ReportingPeriodStart/End         = the PERIOD BEING REPORTED ON, derived server-side from FinancialYear + Quarter
//                                      (ReportingPeriod.Derive). This is what the report is *about*, and what the
//                                      exported ZIP's summary.csv calls "Reporting period".
// The two coincide only when the collection was published with WindowSameAsReportingPeriod = true.
public record OrgSubmissionDetailDto(
    Guid CollectionId,
    string CollectionName,
    string CollectionStatus,
    DateOnly? StartDate,
    DateOnly? Deadline,
    int? Quarter,
    DateOnly? ReportingPeriodStart,
    DateOnly? ReportingPeriodEnd,
    Guid OrganisationId,
    string OrganisationName,
    string Status,
    bool HasSubmission,
    string? SubmittedByName,
    string? SubmittedByEmail,
    DateTime? SubmittedAt,
    string? ReferenceNumber,
    // Trailing, optional (BLUM-7859 AC2): a maturity assessment is read inline rather than downloaded per section, so
    // its detail carries the per-section statuses (catalogue order, catalogue titles) and — once submitted — the frozen
    // snapshot the LA saw at submit time. Both are null for a quarterly window.
    string? CollectionType = null,
    string? FinancialYear = null,
    IReadOnlyList<MaturitySectionSummaryDto>? MaturitySections = null,
    IReadOnlyList<MaturitySnapshotSectionDto>? MaturitySnapshot = null,
    long? ExportSizeBytes = null);

// Result of a one-off "regenerate report snapshots" admin action (BLUM-7130 CSV review): re-freezes every
// already-submitted submission in a collection so the DHSC-wide export's per-organisation section files
// (funding-spend.csv / funding-summary.csv) stop silently omitting submissions whose frozen snapshot predates a
// header-shape change. SkippedOrganisationIds names who was skipped and why, rather than just a count, so an
// admin can tell "everything regenerated cleanly" from "something needs a second look".
public record RegenerateReportSnapshotsResultDto(
    int Regenerated,
    IReadOnlyList<RegenerateReportSnapshotsSkippedDto> Skipped);

public record RegenerateReportSnapshotsSkippedDto(Guid OrganisationId, string Reason);