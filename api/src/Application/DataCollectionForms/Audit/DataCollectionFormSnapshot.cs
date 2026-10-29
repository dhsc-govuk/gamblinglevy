using System.Text.Json;
using Domain.DataCollectionForms;

namespace Application.DataCollectionForms.Audit;

internal static class DataCollectionFormSnapshot
{
    // Module-level fields plus the ids of its children; each section/question change has its own row.
    public static string Serialize(FormModule module)
    {
        return JsonSerializer.Serialize(new
        {
            id = module.Id.Value,
            code = module.Code,
            name = module.Name,
            description = module.Description,
            displayOrder = module.DisplayOrder,
            isActive = module.IsActive,
            status = module.Status.ToString(),
            publishedAt = module.PublishedAt,
            publishedBy = module.PublishedBy?.Value,
            sectionIds = module.Sections.OrderBy(s => s.SectionNumber).Select(s => s.Id.Value),
            questionIds = module.Questions.OrderBy(q => q.DisplayOrder).Select(q => q.Id.Value),
        });
    }

    public static string Serialize(DcFormSection section)
    {
        return JsonSerializer.Serialize(new
        {
            id = section.Id.Value,
            formModuleId = section.FormModuleId.Value,
            sectionNumber = section.SectionNumber,
            title = section.Title,
            description = section.Description,
            helpText = section.HelpText,
            isActive = section.IsActive,
        });
    }

    public static string Serialize(DcFormQuestion question)
    {
        return JsonSerializer.Serialize(new
        {
            id = question.Id.Value,
            formModuleId = question.FormModuleId.Value,
            sectionId = question.SectionId?.Value,
            code = question.Code,
            label = question.Label,
            questionType = question.QuestionType.ToString(),
            isRequired = question.IsRequired,
            isActive = question.IsActive,
            displayOrder = question.DisplayOrder,
            hint = question.Hint,
            helpText = question.HelpText,
            errorMessage = question.ErrorMessage,
            maxLength = question.MaxLength,
            maxLengthErrorMessage = question.MaxLengthErrorMessage,
            options = question.Options
                .OrderBy(o => o.DisplayOrder)
                .Select(o => new { value = o.Value, label = o.Label, displayOrder = o.DisplayOrder, isActive = o.IsActive }),
        });
    }

    public static string SerializeSectionOrder(FormModule module)
    {
        return SerializeOrder(module.Sections.OrderBy(s => s.SectionNumber).Select(s => s.Id.Value));
    }

    public static string SerializeQuestionOrder(FormModule module)
    {
        return SerializeOrder(module.Questions.OrderBy(q => q.DisplayOrder).Select(q => q.Id.Value));
    }

    // Reorders record the ordered id list rather than whole entities: the order is the only thing that changed.
    private static string SerializeOrder(IEnumerable<Guid> orderedIds)
    {
        return JsonSerializer.Serialize(new { orderedIds });
    }
}