namespace Api.Dtos;

public record FormQuestionOptionDto(string Value, string Label, int DisplayOrder);

// Structural superset of the frontend GovUKDynamicQuestionRenderer's FormQuestion interface (adds SectionId),
// so the same shape round-trips from authoring into the LA-facing consumption flow.
public record FormQuestionDto(
    Guid Id,
    Guid SectionId,
    string Code,
    string Label,
    string? Hint,
    string? HelpText,
    string? HelpTextSummary,
    string QuestionType,
    bool IsRequired,
    bool IsActive,
    int DisplayOrder,
    string? ConditionalQuestionCode,
    string? ConditionalValue,
    IReadOnlyList<FormQuestionOptionDto> Options);

public record FormSectionDto(
    Guid Id,
    Guid? CategoryId,
    Guid? ActivityTaskId,
    string Title,
    string? Description,
    int DisplayOrder,
    bool IsActive);

public record FormSchemaDto(
    IReadOnlyList<FormSectionDto> Sections,
    IReadOnlyList<FormQuestionDto> Questions);

public record QuestionOptionRequestDto(string Value, string Label, int DisplayOrder);

public record CreateSectionRequestDto(string? Title, string? Description, Guid? CategoryId = null);

public record UpdateSectionRequestDto(string? Title, string? Description, Guid? CategoryId = null);

public record ReorderRequestDto(IReadOnlyList<Guid>? OrderedIds);

public record ReorderQuestionsRequestDto(Guid SectionId, IReadOnlyList<Guid>? OrderedIds);

public record CreateQuestionRequestDto(
    Guid SectionId,
    string? Code,
    string? Label,
    string? QuestionType,
    bool IsRequired,
    string? Hint,
    string? HelpText,
    string? HelpTextSummary,
    string? ConditionalQuestionCode,
    string? ConditionalValue,
    IReadOnlyList<QuestionOptionRequestDto>? Options);

public record UpdateQuestionRequestDto(
    Guid SectionId,
    string? Label,
    string? QuestionType,
    bool IsRequired,
    bool IsActive,
    string? Hint,
    string? HelpText,
    string? HelpTextSummary,
    string? ConditionalQuestionCode,
    string? ConditionalValue,
    IReadOnlyList<QuestionOptionRequestDto>? Options);