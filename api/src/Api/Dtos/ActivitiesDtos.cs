namespace Api.Dtos;

public record ActivityAnswerDto(string QuestionCode, string? Value);

// A key result on an activity: a standard selection (KeyResultCode set, Text snapshots the definition label) or a
// custom "Other" entry (KeyResultCode null).
public record ExpectedResultDto(string? KeyResultCode, string Text);

public record ActivityDto(
    Guid Id,
    Guid OrganisationId,
    string Title,
    string? Name,
    Guid? CategoryId,
    string? CategoryName,
    Guid? TaskId,
    string? TaskName,
    string? CustomTaskText,
    string? Status,
    bool IsDraft,
    int? StartMonth,
    int? StartYear,
    string? ClosureReason,
    IReadOnlyList<ExpectedResultDto> ExpectedResults,
    IReadOnlyList<ActivityAnswerDto> Answers,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public record ActivitySummaryDto(
    Guid Id,
    string Title,
    Guid? CategoryId,
    string? CategoryName,
    Guid? TaskId,
    string? TaskName,
    string? Status,
    bool IsDraft,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public record TaxonomyCategoryDto(Guid Id, string Name);

public record TaxonomyTaskDto(Guid Id, string Name, Guid CategoryId);

// A standard key result option for one activity type — drives the create/edit "expected results" checkbox list.
public record TaxonomyKeyResultDto(string Code, Guid TaskId, string Label);

public record ActivityTaxonomyDto(
    IReadOnlyList<TaxonomyCategoryDto> Categories,
    IReadOnlyList<TaxonomyTaskDto> Tasks,
    IReadOnlyList<TaxonomyKeyResultDto> KeyResults);

public record ActivityAnswerInputDto(string QuestionCode, string? Value);

public record ExpectedResultInputDto(string? KeyResultCode, string? Text);

public record CreateActivityRequestDto(
    string? Title,
    Guid? CategoryId,
    Guid? TaskId,
    string? CustomTaskText,
    string? Status,
    int? StartMonth,
    int? StartYear,
    bool SaveAsDraft,
    IReadOnlyList<ExpectedResultInputDto>? ExpectedResults = null,
    IReadOnlyList<ActivityAnswerInputDto>? Answers = null,
    string? ClosureReason = null);

public record UpdateActivityRequestDto(
    string? Title,
    Guid? CategoryId,
    Guid? TaskId,
    string? CustomTaskText,
    string? Status,
    int? StartMonth,
    int? StartYear,
    bool SaveAsDraft,
    IReadOnlyList<ExpectedResultInputDto>? ExpectedResults = null,
    IReadOnlyList<ActivityAnswerInputDto>? Answers = null,
    string? ClosureReason = null);