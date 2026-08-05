using Api.Dtos;
using Api.Services.Abstract;
using Application.Common.Interfaces.Queries;
using Domain.Categories;
using Domain.Forms;

namespace Api.Services.Implementation;

public class FormDesignerControllerService(IFormSchemaQueries formSchemaQueries) : IFormDesignerControllerService
{
    public FormSectionDto ToDto(FormSection section) =>
        new(section.Id.Value, section.CategoryId?.Value, section.ActivityTaskId?.Value, section.Title, section.Description, section.DisplayOrder, section.IsActive);

    public FormQuestionDto ToDto(FormQuestion question) =>
        new(
            question.Id.Value,
            question.SectionId.Value,
            question.Code,
            question.Label,
            question.Hint,
            question.HelpText,
            question.HelpTextSummary,
            question.QuestionType.ToString(),
            question.IsRequired,
            question.IsActive,
            question.DisplayOrder,
            question.ConditionalQuestionCode,
            question.ConditionalValue,
            question.Options
                .OrderBy(o => o.DisplayOrder)
                .Select(o => new FormQuestionOptionDto(o.Value, o.Label, o.DisplayOrder))
                .ToList());

    public async Task<FormSchemaDto> GetFullSchemaAsync(CancellationToken cancellationToken)
    {
        var schema = await formSchemaQueries.GetFullSchemaAsync(cancellationToken);
        return ToSchemaDto(schema);
    }

    public async Task<FormSchemaDto> GetActiveSchemaAsync(CancellationToken cancellationToken)
    {
        var schema = await formSchemaQueries.GetActiveSchemaAsync(cancellationToken);
        return ToSchemaDto(schema);
    }

    public async Task<FormSchemaDto> GetActiveSchemaForCategoryAsync(Guid? categoryId, CancellationToken cancellationToken)
    {
        var scoped = categoryId.HasValue ? new CategoryId(categoryId.Value) : null;
        var schema = await formSchemaQueries.GetActiveSchemaForCategoryAsync(scoped, cancellationToken);
        return ToSchemaDto(schema);
    }

    private FormSchemaDto ToSchemaDto(FormSchemaData schema) =>
        new(
            schema.Sections.Select(ToDto).ToList(),
            schema.Questions.Select(ToDto).ToList());
}