using Application.DataCollectionForms.Versions;
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
    // BLUM-9102: set only on a publish while BLUM_9087 is on — the version this publish saved, or (VersionCreated
    // false) the unchanged version it matched. Null everywhere else.
    public int? LatestVersionNumber { get; init; }

    public bool? VersionCreated { get; init; }

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

// BLUM-9102: a saved, unchangeable version of a form module. PublishedByName is null when the publisher's account no
// longer exists.
public record FormModuleVersionSummaryDto(Guid Id, int VersionNumber, DateTime PublishedAt, Guid PublishedBy, string? PublishedByName)
{
    public static FormModuleVersionSummaryDto FromDomainModel(FormModuleVersion version, string? publishedByName) =>
        new(version.Id.Value, version.VersionNumber, version.PublishedAt, version.PublishedBy.Value, publishedByName);
}

// BLUM-9086: one version of a form module and the collections that record it, empty when none does.
public record FormModuleVersionCollectionsDto(Guid VersionId, int VersionNumber, IReadOnlyList<CollectionSummaryDto> Collections);

// One version with the content it saved, read back from its stored definition.
public record FormModuleVersionDto(
    Guid Id,
    int VersionNumber,
    DateTime PublishedAt,
    Guid PublishedBy,
    string? PublishedByName,
    string Code,
    string Name,
    string? Description,
    IReadOnlyList<FormSectionDefinition> Sections,
    IReadOnlyList<FormQuestionDefinition> Questions)
{
    public static FormModuleVersionDto FromDomainModel(FormModuleVersion version, FormModuleDefinitionDocument definition, string? publishedByName) =>
        new(
            version.Id.Value,
            version.VersionNumber,
            version.PublishedAt,
            version.PublishedBy.Value,
            publishedByName,
            definition.Code,
            definition.Name,
            definition.Description,
            definition.Sections.OrderBy(s => s.SectionNumber).ToList(),
            definition.Questions.OrderBy(q => q.DisplayOrder).ToList());
}