using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Domain.Forms;
using LanguageExt;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class FormQuestionRepository(ApplicationDbContext dbContext, IDateTimeProvider dateTimeProvider)
    : RepositoryBase<FormQuestion, FormQuestionId>(dbContext), IFormQuestionRepository
{
    // Tracked + options, for mutation (the update flow mutates the options collection then SaveChangesAsync).
    public async Task<Option<FormQuestion>> GetByIdAsync(FormQuestionId id, CancellationToken cancellationToken = default)
    {
        var entity = await Context.FormQuestions
            .Include(x => x.Options)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        return entity ?? Option<FormQuestion>.None;
    }

    public async Task<Option<FormQuestion>> FindByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var entity = await Context.FormQuestions
            .AsNoTracking()
            .Include(x => x.Options)
            .FirstOrDefaultAsync(x => x.Code == code, cancellationToken);
        return entity ?? Option<FormQuestion>.None;
    }

    public async Task<IReadOnlyList<FormQuestion>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await Context.FormQuestions
            .AsNoTracking()
            .Include(x => x.Options)
            .OrderBy(x => x.DisplayOrder)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> CountBySectionAsync(FormSectionId sectionId, CancellationToken cancellationToken = default)
    {
        return await Context.FormQuestions.CountAsync(x => x.SectionId == sectionId, cancellationToken);
    }

    public async Task<bool> AnyReferencingConditionalAsync(string parentCode, FormQuestionId excludingId, CancellationToken cancellationToken = default)
    {
        return await Context.FormQuestions
            .AnyAsync(x => x.ConditionalQuestionCode == parentCode && x.Id != excludingId, cancellationToken);
    }

    public async Task<int> GetMaxDisplayOrderForSectionAsync(FormSectionId sectionId, CancellationToken cancellationToken = default)
    {
        var sectionQuestions = Context.FormQuestions.Where(x => x.SectionId == sectionId);
        return await sectionQuestions.AnyAsync(cancellationToken)
            ? await sectionQuestions.MaxAsync(x => x.DisplayOrder, cancellationToken)
            : 0;
    }

    public async Task ReorderAsync(FormSectionId sectionId, IReadOnlyList<Guid> orderedIds, CancellationToken cancellationToken = default)
    {
        var ids = orderedIds.Select(x => new FormQuestionId(x)).ToList();
        var questions = await Context.FormQuestions
            .Where(x => x.SectionId == sectionId && ids.Contains(x.Id))
            .ToListAsync(cancellationToken);
        var orderById = orderedIds.Select((id, index) => (id, index)).ToDictionary(x => x.id, x => x.index);

        foreach (var question in questions)
        {
            if (orderById.TryGetValue(question.Id.Value, out var index))
            {
                question.SetDisplayOrder(index, dateTimeProvider.GetUtcNow());
            }
        }

        await Context.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> DeleteAsync(FormQuestionId id, CancellationToken cancellationToken = default)
    {
        await Context.FormQuestionOptions.Where(x => x.QuestionId == id).ExecuteDeleteAsync(cancellationToken);
        return await Context.FormQuestions.Where(x => x.Id == id).ExecuteDeleteAsync(cancellationToken);
    }
}