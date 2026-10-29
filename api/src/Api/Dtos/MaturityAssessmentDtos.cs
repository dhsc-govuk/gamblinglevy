namespace Api.Dtos;

// --- LA-facing maturity assessment (BLUM-7855 / 7856 / 7858) ---

// The "Manage your maturity assessment" context for one collection. AssessmentId is null until the LA opens the
// task list (which starts it); SubmittedAt/ReferenceNumber are null until finalised. Status is the derived per-org
// status (not_started / in_progress / completed / overdue). FinancialYearEndYear names the assessment on the client
// ("Maturity assessment for 2027").
public record MaturityAssessmentContextDto(
    Guid CollectionId,
    string CollectionName,
    string FinancialYear,
    int FinancialYearEndYear,
    DateOnly ReportingPeriodStart,
    DateOnly ReportingPeriodEnd,
    DateOnly SubmissionDeadline,
    string CollectionStatus,
    bool IsOpen,
    string LocalAuthorityName,
    Guid? AssessmentId,
    string Status,
    DateTime? SubmittedAt,
    string? ReferenceNumber);

public record MaturitySectionSummaryDto(string Code, string Title, string Status);

// IsPrefilled / PrefillSources drive the "Your maturity assessment is pre-filled" banner: true while any saved
// answer still carries a non-user source. CanSubmit is the submit gate (every section Completed, not submitted).
public record MaturityTaskListDto(
    Guid AssessmentId,
    IReadOnlyList<MaturitySectionSummaryDto> Sections,
    bool CanSubmit,
    bool Submitted,
    string? ReferenceNumber,
    DateTime? SubmittedAt,
    bool IsPrefilled,
    IReadOnlyList<string> PrefillSources);

// A resolved maturity question in the ReportQuestionDto-compatible shape the frontend renderer consumes, plus the
// annual assessment's extras: Page (the page slug within its Section), GroupLabel (the matrix heading consecutive
// items share), SecondaryOptions (the funded-roles time bands) and ExclusiveOptionValue (the "None" that stands alone:
// a checkbox option, or the funded-roles repeater's only row).
public record MaturityQuestionDto(
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
    IReadOnlyList<ReportQuestionOptionDto> SecondaryOptions,
    string Section,
    string Page,
    string? GroupLabel,
    string? ExclusiveOptionValue,
    string? ErrorMessage,
    int? MaxLength,
    string? MaxLengthErrorMessage);

public record MaturityPageVisibilityDto(string QuestionCode, IReadOnlyList<string> Values);

public record MaturityPageDto(
    string Code,
    string Title,
    MaturityPageVisibilityDto? VisibleWhen,
    IReadOnlyList<MaturityQuestionDto> Questions);

public record MaturityAnswerDto(string QuestionCode, string? Value, string Source);

public record MaturitySectionDto(
    Guid AssessmentId,
    string Code,
    string Title,
    string Status,
    IReadOnlyList<MaturityPageDto> Pages,
    IReadOnlyList<MaturityAnswerDto> Answers);

public record MaturityAnswerInputDto(string QuestionCode, string? Value);

public record SaveMaturityAnswersRequestDto(
    string PageCode,
    bool SaveAsDraft,
    IReadOnlyList<MaturityAnswerInputDto>? Answers = null);

public record MaturitySectionStateDto(string SectionCode, string Status);

public record SubmitMaturityAssessmentResultDto(string ReferenceNumber, DateTime SubmittedAt);

// --- Read-only submitted assessment (BLUM-7859) ---

public record MaturitySnapshotRowDto(string Code, string Question, string Answer, string? Value, string? GroupLabel);

public record MaturitySnapshotPageDto(string Code, string Title, IReadOnlyList<MaturitySnapshotRowDto> Rows);

public record MaturitySnapshotSectionDto(string Code, string Title, IReadOnlyList<MaturitySnapshotPageDto> Pages);

public record SubmittedMaturityAssessmentDto(
    Guid CollectionId,
    string CollectionName,
    string FinancialYear,
    int FinancialYearEndYear,
    string LocalAuthorityName,
    string ReferenceNumber,
    DateTime SubmittedAt,
    string? SubmittedByName,
    string? SubmittedByEmail,
    IReadOnlyList<MaturitySnapshotSectionDto> Sections);