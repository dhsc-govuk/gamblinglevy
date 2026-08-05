using Application.Common.Interfaces.Queries;
using Domain.Categories;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class FormSchemaQueries(ApplicationDbContext dbContext) : IFormSchemaQueries
{
    public async Task<FormSchemaData> GetActiveSchemaAsync(CancellationToken cancellationToken = default)
    {
        var sections = await dbContext.FormSections
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.DisplayOrder)
            .ToListAsync(cancellationToken);

        var questions = await dbContext.FormQuestions
            .AsNoTracking()
            .Include(x => x.Options)
            .Where(x => x.IsActive)
            .OrderBy(x => x.DisplayOrder)
            .ToListAsync(cancellationToken);

        return new FormSchemaData(sections, questions);
    }

    public async Task<FormSchemaData> GetActiveSchemaForCategoryAsync(
        CategoryId? categoryId,
        CancellationToken cancellationToken = default)
    {
        var full = await GetActiveSchemaAsync(cancellationToken);

        // A section applies when it is global (no category) or matches the activity's category. Filtered in
        // memory — the section set is small and this avoids value-converter translation edge cases.
        var sections = full.Sections
            .Where(x => x.CategoryId is null || (categoryId is not null && x.CategoryId.Value == categoryId.Value))
            .ToList();

        var sectionIds = sections.Select(s => s.Id).ToHashSet();
        var questions = full.Questions.Where(q => sectionIds.Contains(q.SectionId)).ToList();

        return new FormSchemaData(sections, questions);
    }

    public async Task<FormSchemaData> GetFullSchemaAsync(CancellationToken cancellationToken = default)
    {
        var sections = await dbContext.FormSections
            .AsNoTracking()
            .OrderBy(x => x.DisplayOrder)
            .ToListAsync(cancellationToken);

        var questions = await dbContext.FormQuestions
            .AsNoTracking()
            .Include(x => x.Options)
            .OrderBy(x => x.DisplayOrder)
            .ToListAsync(cancellationToken);

        return new FormSchemaData(sections, questions);
    }
}