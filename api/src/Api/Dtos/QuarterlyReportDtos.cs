using System.Text.Json.Serialization;

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
    string Section,

    // Question Protocol copy (BLUM-7620): the "empty on submit" message, the free-text character limit (which
    // makes the client render a GDS character count) and the message shown when it is exceeded. All three are
    // mirrored client-side so the message a user sees before submitting matches the one the API would return.
    string? ErrorMessage = null,
    int? MaxLength = null,
    string? MaxLengthErrorMessage = null)
{
    // Options DHSC has withdrawn from an Activity delivery question (BLUM-9104). Never sent: an LA is offered only
    // Options. Kept so ReportAnswerPresenter can still label an answer given with one, in the report snapshot and the
    // activity-record export.
    [JsonIgnore]
    public IReadOnlyList<ReportQuestionOptionDto> WithdrawnOptions { get; init; } = [];
}

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
// spend from EVERY earlier quarter this activity has been reported in, earlier financial years included (the
// per-activity "Total spent so far" inset, BLUM-7703) — a different question from the year-scoped
// FundingSpendSummaryDto.SpendToDate rendered beside it, and from TotalActualToDate below, which is this year only
// and includes the reporting quarter.
//
// CategoryName / ActualByQuarter / ForecastByQuarter / TotalActualToDate / ProjectedActivitySpend exist only for the
// DHSC/LA CSV exports (BLUM-7130 CSV review) — deliberately additive rather than redefining ActualThisQuarter /
// TotalSpentSoFar / Forecast, so the live "Record funding spent"/"Forecast spend" screens keep showing exactly what
// they do today. ActualByQuarter covers Q1-4 (null for a quarter not yet reported); ForecastByQuarter covers Q2-4
// only (Q1 can never be forecast — nothing precedes it in the financial year) and holds the MOST RECENTLY RECORDED
// estimate for that quarter, which may come from an earlier submission than the one being exported.
//
// ForecastOnly marks a Planned activity (BLUM-7672): it belongs on the "Forecast spend" screen and not on "Activity
// spending", so ActualThisQuarter is always null for it and the client leaves it off the actuals inputs. It keeps its
// row in every quarter, Q4 included (no forecast inputs there, but the export still reads its earlier forecasts from
// ForecastByQuarter), and earlier quarters' actuals (recorded before it was reverted to Planned) still appear in
// ActualByQuarter, so its export row carries its own history. The flag is the server's call, not a status the client
// interprets, so the rule for which screen an activity belongs on lives in FundingSpendContextResolver alone.
public record FundingSpendActivityDto(
    Guid ActivityId,
    string Title,
    string? TaskName,
    decimal? ActualThisQuarter,
    decimal TotalSpentSoFar,
    IReadOnlyDictionary<int, decimal?> Forecast,
    string? CategoryName,
    IReadOnlyDictionary<int, decimal?> ActualByQuarter,
    IReadOnlyDictionary<int, decimal?> ForecastByQuarter,
    decimal TotalActualToDate,
    decimal ProjectedActivitySpend,
    bool ForecastOnly);

// Export-only (BLUM-7607). Actual spend recorded against an activity that has no row in FundingSpendDto.Activities —
// soft-deleted, or whose setup is no longer complete (a Planned activity is not one of these: it has a forecast-only
// row that carries its earlier actuals itself, BLUM-7672) — so no amount an LA recorded can disappear between the
// quarterly columns and the organisation total. Deliberately NOT merged into FundingSpendDto.Activities: that list
// drives the LA "Activity spending" inputs, and SaveFundingActualsCommandHandler filters every posted amount through
// the actual-spend set, so surfacing one of these on that screen would take an amount and silently discard it.
//
// Actuals only. Forecast cells render blank and the projection is just TotalActualToDate: a forecast is not money
// recorded as spent, and orphaned forecasts are already absent from BOTH the Q{n} Forecast columns and Total
// Forecast, so leaving them out breaks no reconciliation.
//
// ActivityId is null for the catch-all row — spend rows reference an activity by id with no foreign key, so an id
// that resolves to no activity at all is possible and must still carry its money.
public record FundingSpendUnreportedActivityDto(
    Guid? ActivityId,
    string Title,
    string? CategoryName,
    IReadOnlyDictionary<int, decimal?> ActualByQuarter,
    decimal TotalActualToDate);

// The "Summary for all activities" figures the backend can compute. TotalBudgetForYear / BudgetRemaining are
// intentionally absent — no data source exists for an LA annual budget (follow-up).
//
// TotalSpendToDate / RemainingBudget / TotalForecast / ProjectedVariance exist only for the CSV exports (BLUM-7130
// CSV review) — additive, not a redefinition of SpendToDate/ExpectedSpendForYear, for the same reason as the
// per-activity fields above. TotalSpendToDate (unlike SpendToDate) includes the current quarter's own actual spend.
//
// TotalSpendToDate is the SUM of TotalActualByQuarter, not a second roll-up of its own (BLUM-7607). It used to come
// from ISpendReportTotals, which counts every spend row, while the quarterly columns counted only reportable
// activities — so an LA that deleted an activity after reporting spend against it produced a total its own quarterly
// figures could not add up to. One derivation, and the columns reconcile with the total by construction. It also
// stays equal to SpendToDate + TotalSpendThisQuarter, so the LA's own summary box reconciles with it, and
// ExpectedSpendForYear (= TotalSpendToDate + TotalForecast) reconciles with both.
public record FundingSpendSummaryDto(
    decimal TotalGrantAllocation,
    decimal TotalSpendThisQuarter,
    decimal ExpectedSpendForYear,
    decimal SpendToDate,
    decimal TotalSpendToDate,
    decimal RemainingBudget,
    decimal TotalForecast,
    decimal ProjectedVariance,
    // The financial year TotalGrantAllocation belongs to, and the upcoming year's allocation alongside it
    // (BLUM-7608). Both are export-only: they name the money rather than changing any figure derived from it.
    // Trailing and defaulted so every existing positional construction still compiles.
    string? GrantAllocationFinancialYear = null,
    decimal? NextYearGrantAllocation = null,
    string? NextYearFinancialYear = null);

// OtherActualByQuarter / OtherForecastByQuarter / OtherTotalActualToDate / OtherProjectedSpend mirror the same new
// export-only fields on FundingSpendActivityDto, for the "Other (not linked to an activity)" line — see that
// record's comment for the exact semantics.
public record FundingSpendDto(
    Guid SubmissionId,
    int CurrentQuarter,
    string FinancialYear,
    string SectionStatus,
    IReadOnlyList<FundingSpendQuarterDto> RemainingQuarters,
    IReadOnlyList<FundingSpendActivityDto> Activities,
    decimal? OtherActualThisQuarter,
    IReadOnlyDictionary<int, decimal?> OtherForecast,
    IReadOnlyDictionary<int, decimal?> OtherActualByQuarter,
    IReadOnlyDictionary<int, decimal?> OtherForecastByQuarter,
    decimal OtherTotalActualToDate,
    decimal OtherProjectedSpend,
    FundingSpendSummaryDto Summary,
    IReadOnlyDictionary<int, decimal?> TotalActualByQuarter,
    IReadOnlyList<FundingSpendUnreportedActivityDto> UnreportedActivities);

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
    DateTime? SubmittedAt,
    // Trailing, optional: a maturity-assessment row (Type = maturity_assessment, Quarter = null) is named by its
    // financial year on the LA history list (BLUM-7859 AC1).
    string? FinancialYear = null);

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
    DateTime? SubmittedAt,
    // Trailing, optional (BLUM-7859): the collection type lets the client pick the maturity read-only view, whose
    // sections come from maturity-assessment/{id}/submitted; ReferenceNumber is the finalised report's reference.
    string? Type = null,
    string? FinancialYear = null,
    string? ReferenceNumber = null,
    // The finalised ZIP's byte length, captioning the download ("ZIP, 389 KB", BLUM-9018). Null until submitted, or
    // when it could not be measured — see ReportExportSizeService.
    long? ExportSizeBytes = null);