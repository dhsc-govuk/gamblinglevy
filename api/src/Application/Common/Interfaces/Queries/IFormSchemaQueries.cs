using Domain.Categories;
using Domain.Forms;

namespace Application.Common.Interfaces.Queries;

// The full activity-record form schema: ordered sections plus their questions (with options).
public record FormSchemaData(IReadOnlyList<FormSection> Sections, IReadOnlyList<FormQuestion> Questions)
{
    // The questions applicable to an activity of the given type: those in unscoped sections plus sections
    // scoped to that exact type. Used for answer validation and persistence.
    public IReadOnlyList<FormQuestion> QuestionsForTask(ActivityTaskId? taskId)
    {
        var applicableSectionIds = Sections.Where(s => s.AppliesToTask(taskId)).Select(s => s.Id).ToHashSet();
        return Questions.Where(q => applicableSectionIds.Contains(q.SectionId)).ToList();
    }
}

public interface IFormSchemaQueries
{
    // Active sections + active questions only — used by the LA-facing render and server-side answer validation.
    Task<FormSchemaData> GetActiveSchemaAsync(CancellationToken cancellationToken = default);

    // Active schema scoped to a single activity category: sections for that category plus global sections
    // (CategoryId == null), with their active questions. Used by the create/edit flow + answer validation.
    Task<FormSchemaData> GetActiveSchemaForCategoryAsync(CategoryId? categoryId, CancellationToken cancellationToken = default);

    // All sections + all questions (including inactive) — used by the DHSC Form Designer authoring view.
    Task<FormSchemaData> GetFullSchemaAsync(CancellationToken cancellationToken = default);
}