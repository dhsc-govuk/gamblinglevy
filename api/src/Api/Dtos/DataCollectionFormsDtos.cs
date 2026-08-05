using Domain.DataCollectionForms;

namespace Api.Dtos;

// Request DTOs mirror the forms-builder command records (Application.DataCollectionForms.Commands.*). The
// controller maps these into the matching command before dispatch. Response DTOs project the FormModule
// aggregate (with its owned sections / questions / options) back to the authoring client.

public record CreateFormModuleRequestDto(string? Code, string? Name, string? Description);

public record UpdateFormModuleRequestDto(string? Name, string? Description);

public record CreateFormSectionRequestDto(string? Title, string? Description, string? HelpText);

public record UpdateFormSectionRequestDto(string? Title, string? Description, string? HelpText);

// ReorderRequestDto ({ OrderedIds }) is shared with the activity Form Designer surface — declared in
// FormDesignerDtos.cs (same Api.Dtos namespace) and reused here for sections/questions reorder.

// A forms-builder choice option supplied on create/update. Distinct from FormDesigner's
// QuestionOptionRequestDto (which carries an explicit DisplayOrder) — here order is positional.
public record FormModuleQuestionOptionRequestDto(string Value, string Label);

public record CreateFormQuestionRequestDto(
    Guid? SectionId,
    string? Code,
    string? Label,
    string? QuestionType,
    bool IsRequired,
    string? Hint,
    string? HelpText,
    IReadOnlyList<FormModuleQuestionOptionRequestDto>? Options);

public record UpdateFormQuestionRequestDto(
    Guid? SectionId,
    string? Label,
    string? QuestionType,
    bool IsRequired,
    string? Hint,
    string? HelpText,
    IReadOnlyList<FormModuleQuestionOptionRequestDto>? Options);

public record DcFormQuestionOptionDto(Guid Id, string Value, string Label, int DisplayOrder, bool IsActive)
{
    public static DcFormQuestionOptionDto FromDomainModel(DcFormQuestionOption option) =>
        new(option.Id.Value, option.Value, option.Label, option.DisplayOrder, option.IsActive);
}

public record DcFormQuestionDto(
    Guid Id,
    Guid FormModuleId,
    Guid? SectionId,
    string Code,
    string Label,
    string QuestionType,
    int DisplayOrder,
    bool IsRequired,
    string? Hint,
    string? HelpText,
    bool IsActive,
    IReadOnlyList<DcFormQuestionOptionDto> Options)
{
    public static DcFormQuestionDto FromDomainModel(DcFormQuestion question) =>
        new(
            question.Id.Value,
            question.FormModuleId.Value,
            question.SectionId?.Value,
            question.Code,
            question.Label,
            question.QuestionType.ToString(),
            question.DisplayOrder,
            question.IsRequired,
            question.Hint,
            question.HelpText,
            question.IsActive,
            question.Options
                .OrderBy(o => o.DisplayOrder)
                .Select(DcFormQuestionOptionDto.FromDomainModel)
                .ToList());
}

public record DcFormSectionDto(
    Guid Id,
    Guid FormModuleId,
    int SectionNumber,
    string Title,
    string? Description,
    string? HelpText,
    bool IsActive)
{
    public static DcFormSectionDto FromDomainModel(DcFormSection section) =>
        new(
            section.Id.Value,
            section.FormModuleId.Value,
            section.SectionNumber,
            section.Title,
            section.Description,
            section.HelpText,
            section.IsActive);
}

public record FormModuleDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    int DisplayOrder,
    bool IsActive,
    string Status,
    DateTime CreatedAt,
    DateTime? UpdatedAt)
{
    public static FormModuleDto FromDomainModel(FormModule module) =>
        new(
            module.Id.Value,
            module.Code,
            module.Name,
            module.Description,
            module.DisplayOrder,
            module.IsActive,
            module.Status.ToString(),
            module.CreatedAt,
            module.UpdatedAt);
}

public record FormModuleWithChildrenDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    int DisplayOrder,
    bool IsActive,
    string Status,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    IReadOnlyList<DcFormSectionDto> Sections,
    IReadOnlyList<DcFormQuestionDto> Questions)
{
    public static FormModuleWithChildrenDto FromDomainModel(FormModule module) =>
        new(
            module.Id.Value,
            module.Code,
            module.Name,
            module.Description,
            module.DisplayOrder,
            module.IsActive,
            module.Status.ToString(),
            module.CreatedAt,
            module.UpdatedAt,
            module.Sections
                .OrderBy(s => s.SectionNumber)
                .Select(DcFormSectionDto.FromDomainModel)
                .ToList(),
            module.Questions
                .OrderBy(q => q.DisplayOrder)
                .Select(DcFormQuestionDto.FromDomainModel)
                .ToList());
}