namespace Api.Dtos;

// LA name, reporting window and report state for the "Submit your quarterly report" intro page. Null when the
// organisation has no open assigned quarterly collection (the endpoint returns an empty body). SubmissionId is null
// until the LA clicks "Create report"; SubmittedAt/ReferenceNumber are null until the report is finalised.
public record QuarterlyReportContextDto(
    Guid CollectionId,
    string CollectionName,
    int Quarter,
    string FinancialYear,
    DateOnly ReportingPeriodStart,
    DateOnly ReportingPeriodEnd,
    DateOnly SubmissionDeadline,
    string LocalAuthorityName,
    Guid? SubmissionId,
    DateTime? SubmittedAt,
    string? ReferenceNumber);

// One row of the "Manage your quarterly report" task list. IsAvailable is false until the activities list has been
// confirmed (rendered "Cannot start yet"); ReviewRequired marks a started section the LA must revisit because the
// activity list changed after confirmation.
public record SubmissionSectionDto(
    string ModuleCode,
    string ModuleName,
    string Status,
    bool ReviewRequired,
    bool IsAvailable);

// CanSubmit is the report-level submit gate (activities confirmed + every section Completed + nothing flagged for
// review + not already submitted) — drives the "Continue" button on the task list. Submitted/ReferenceNumber expose
// the finalised state so the frontend can route to the read-only confirmation.
public record SubmissionTaskListDto(
    Guid SubmissionId,
    bool ActivitiesConfirmed,
    bool ActivitiesReviewRequired,
    IReadOnlyList<SubmissionSectionDto> Sections,
    bool CanSubmit,
    bool Submitted,
    string? ReferenceNumber);

// Result of POST .../submission/submit — the generated reference number and the moment the report was finalised.
public record SubmitReportResultDto(string ReferenceNumber, DateTime SubmittedAt);

// One row of the "Select an activity" screen: reportable activities (active / closed / finished) with their
// per-activity report progress.
public record ReportableActivityDto(
    Guid ActivityId,
    string Title,
    string? TaskName,
    string ActivityStatus,
    string ReportStatus);

public record ReportQuestionOptionDto(string Value, string Label);

// A resolved quarterly-report question in the FormQuestionDto-compatible shape the frontend renderer consumes.
// Section groups questions onto the "Activity results" ("results") or "Activity delivery" ("delivery") page;
// Suffix/MinValue/MaxValue carry the numeric metadata (percentages render with a "%" suffix and validate 0–100).
public record ReportQuestionDto(
    Guid Id,
    string Code,
    string Label,
    string? Hint,
    string QuestionType,
    bool IsRequired,
    int DisplayOrder,
    string? ConditionalQuestionCode,
    string? ConditionalValue,
    IReadOnlyList<ReportQuestionOptionDto> Options,
    decimal? MinValue,
    decimal? MaxValue,
    string? Suffix,
    string Section);

// The full form for one activity: resolved questions plus current values (saved draft answers merged over the
// prefill mapped from the activity record — saved values win).
public record ActivityReportQuestionsDto(
    Guid ActivityId,
    string ActivityTitle,
    string ActivityStatus,
    string ReportStatus,
    IReadOnlyList<ReportQuestionDto> Questions,
    IReadOnlyList<ActivityAnswerDto> Answers);

public record SaveActivityReportRequestDto(
    bool SaveAsDraft,
    IReadOnlyList<ActivityAnswerInputDto>? Answers = null);

public record ActivityReportStateDto(Guid ActivityId, string ReportStatus);

// --- "Record funding spent" section (BLUM-6612) ---

// One of the remaining quarters of the financial year (the quarters strictly after the reporting quarter, to Q4)
// the "Forecast spend" screen asks the LA to forecast, with its reporting-period dates. Empty for a Q4 report.
public record FundingSpendQuarterDto(int Quarter, DateOnly Start, DateOnly End);

// A reportable activity's spend lines. ActualThisQuarter is the saved "levy funding spent this quarter" (null until
// answered); Forecast holds the saved expected spend keyed by quarter; TotalSpentSoFar is the accumulated actual
// spend from earlier quarters of the same financial year (the per-activity "Total spent so far" inset).
public record FundingSpendActivityDto(
    Guid ActivityId,
    string Title,
    string? TaskName,
    decimal? ActualThisQuarter,
    decimal TotalSpentSoFar,
    IReadOnlyDictionary<int, decimal?> Forecast);

// The "Summary for all activities" figures the backend can compute. TotalBudgetForYear / BudgetRemaining are
// intentionally absent — no data source exists for an LA annual budget (follow-up).
public record FundingSpendSummaryDto(
    decimal TotalGrantAllocation,
    decimal TotalSpendThisQuarter,
    decimal ExpectedSpendForYear,
    decimal SpendToDate);

public record FundingSpendDto(
    Guid SubmissionId,
    int CurrentQuarter,
    string FinancialYear,
    string SectionStatus,
    IReadOnlyList<FundingSpendQuarterDto> RemainingQuarters,
    IReadOnlyList<FundingSpendActivityDto> Activities,
    decimal? OtherActualThisQuarter,
    IReadOnlyDictionary<int, decimal?> OtherForecast,
    FundingSpendSummaryDto Summary);

public record FundingActivitySpendInputDto(Guid? ActivityId, decimal? Amount);

public record SaveFundingActualsRequestDto(
    bool SaveAsDraft,
    decimal? OtherAmount = null,
    IReadOnlyList<FundingActivitySpendInputDto>? Activities = null);

public record FundingQuarterAmountInputDto(int Quarter, decimal? Amount);

public record FundingActivityForecastInputDto(Guid? ActivityId, IReadOnlyList<FundingQuarterAmountInputDto>? Quarters = null);

public record SaveFundingForecastRequestDto(
    bool SaveAsDraft,
    IReadOnlyList<FundingActivityForecastInputDto>? Activities = null,
    IReadOnlyList<FundingQuarterAmountInputDto>? Other = null);

public record FundingSpendStateDto(string SectionStatus);

// --- "Provide strategic feedback" section (BLUM-6613) ---

// One page-group of the feedback form: a stable page slug (carried on each question's Section field so the frontend
// routes questions to the right page / check-card), the section title, and its ordered questions.
public record StrategicFeedbackPageDto(
    string Key,
    string Title,
    IReadOnlyList<ReportQuestionDto> Questions);

public record StrategicFeedbackAnswerDto(string QuestionCode, string? Value);

// The "Provide strategic feedback" section state: the question pages (grouped by form section), the saved answers and
// the rolled-up section status. Quarter/FinancialYear are also on the report context but included for symmetry with
// the other section DTOs.
public record StrategicFeedbackDto(
    Guid SubmissionId,
    int CurrentQuarter,
    string FinancialYear,
    string SectionStatus,
    IReadOnlyList<StrategicFeedbackPageDto> Pages,
    IReadOnlyList<StrategicFeedbackAnswerDto> Answers);

public record StrategicFeedbackAnswerInputDto(string QuestionCode, string? Value);

public record SaveStrategicFeedbackRequestDto(
    bool SaveAsDraft,
    IReadOnlyList<StrategicFeedbackAnswerInputDto>? Answers = null);

public record StrategicFeedbackStateDto(string SectionStatus);

// --- LA submission history + detail (BLUM-6097 / BLUM-6098) ---

// One of the caller's organisation's quarterly collections, projected for the LA "Reports" area. CollectionStatus is
// the collection's own lifecycle status (Open/Closed/Scheduled) — the landing surfaces Open, not-yet-submitted ones;
// SubmissionStatus is the derived per-org status and SubmittedAt drives the "Previously submitted reports" list (rows
// with SubmittedAt set). ReportingPeriodStart/End + Quarter compose the report's display name on the frontend.
public record MyCollectionSummaryDto(
    Guid CollectionId,
    string? Type,
    int? Quarter,
    DateOnly? ReportingPeriodStart,
    DateOnly? ReportingPeriodEnd,
    DateOnly? SubmissionDeadline,
    string CollectionStatus,
    string SubmissionStatus,
    DateTime? SubmittedAt);

// The caller's own submission detail for one collection (BLUM-6098, LA read-only view — no DHSC verification
// controls). HasSubmission drives the empty state; SubmittedByName/Email/SubmittedAt are populated only once the
// report is finalised. The downloadable ZIP is served by the existing quarterly-report/{id}/export endpoint.
// LocalAuthorityName feeds the download link's display text ("Quarterly report for {LA}, {start} to {end}").
public record MySubmissionDetailDto(
    Guid CollectionId,
    int? Quarter,
    DateOnly? ReportingPeriodStart,
    DateOnly? ReportingPeriodEnd,
    string LocalAuthorityName,
    string Status,
    bool HasSubmission,
    string? SubmittedByName,
    string? SubmittedByEmail,
    DateTime? SubmittedAt);