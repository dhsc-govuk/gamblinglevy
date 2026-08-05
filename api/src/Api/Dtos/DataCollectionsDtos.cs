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
    IReadOnlyList<ReminderRequestDto>? Reminders = null);

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
    IReadOnlyList<ReminderRequestDto>? Reminders = null);

public record CollectionFormDto(Guid ModuleId, string? Code, string? Name);

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
    string Status);

public record CollectionFormOptionDto(Guid ModuleId, string Name, DateTime? LastChangedAt);

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
    IReadOnlyList<OrgSubmissionStatusDto> Organisations);

// Deliberately carries no export file size. Producing one meant building the organisation's whole report ZIP in
// memory — for every submitted organisation in the window, up to ~160 of them, on every page load — and the
// Organisations screen never read the field. The size belongs to the detail screen, which shows one organisation
// and actually renders it (OrgSubmissionDetailDto below).
public record OrgSubmissionStatusDto(
    Guid OrganisationId,
    string OrganisationName,
    string Status,
    DateTime? SubmittedAt);

// DHSC submission detail for one organisation against one collection (BLUM-6445). HasSubmission drives the empty
// state; SubmittedByName/Email/SubmittedAt/ReferenceNumber are populated only once the report is finalised.
//
// Deliberately carries no export file size, for the same reason OrgSubmissionStatusDto above does not: the only way
// to know a ZIP's byte length is to build the whole ZIP, so every view of this page rendered and Deflate-compressed
// the organisation's entire report just to caption it — then the download rebuilt it. Measuring also meant either
// letting a broken export 500 the page or swallowing the failure and still offering a download button that would.
// Restoring the caption properly means persisting the size at submit time alongside ReportSnapshotJson.
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
    string? ReferenceNumber);