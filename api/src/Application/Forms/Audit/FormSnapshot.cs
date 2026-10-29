using System.Text.Json;
using Domain.Forms;

namespace Application.Forms.Audit;

internal static class FormSnapshot
{
    public static string Serialize(FormSection section)
    {
        return JsonSerializer.Serialize(new
        {
            id = section.Id.Value,
            title = section.Title,
            description = section.Description,
            categoryId = section.CategoryId?.Value,
            activityTaskId = section.ActivityTaskId?.Value,
            displayOrder = section.DisplayOrder,
            isActive = section.IsActive,
        });
    }

    public static string Serialize(FormQuestion question)
    {
        return JsonSerializer.Serialize(new
        {
            id = question.Id.Value,
            sectionId = question.SectionId.Value,
            code = question.Code,
            label = question.Label,
            hint = question.Hint,
            helpText = question.HelpText,
            helpTextSummary = question.HelpTextSummary,
            questionType = question.QuestionType.ToString(),
            isRequired = question.IsRequired,
            isActive = question.IsActive,
            displayOrder = question.DisplayOrder,
            conditionalQuestionCode = question.ConditionalQuestionCode,
            conditionalValue = question.ConditionalValue,
            errorMessage = question.ErrorMessage,
            maxLength = question.MaxLength,
            maxLengthErrorMessage = question.MaxLengthErrorMessage,
            options = question.Options
                .OrderBy(o => o.DisplayOrder)
                .Select(o => new { value = o.Value, label = o.Label, displayOrder = o.DisplayOrder }),
        });
    }

    // Reorders record the ordered id list rather than whole entities: the order is the only thing that changed.
    public static string SerializeOrder(IEnumerable<Guid> orderedIds)
    {
        return JsonSerializer.Serialize(new { orderedIds });
    }
}