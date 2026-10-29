using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Domain.Forms;
using LanguageExt;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class FormSectionRepository(ApplicationDbContext dbContext, IDateTimeProvider dateTimeProvider)
    : RepositoryBase<FormSection, FormSectionId>(dbContext), IFormSectionRepository
{
    public async Task<Option<FormSection>> GetByIdAsync(FormSectionId id, CancellationToken cancellationToken = default)
    {
        var entity = await Context.FormSections.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        return entity ?? Option<FormSection>.None;
    }

    public async Task<IReadOnlyList<FormSection>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await Context.FormSections.AsNoTracking().OrderBy(x => x.DisplayOrder).ToListAsync(cancellationToken);
    }

    public async Task<int> GetMaxDisplayOrderAsync(CancellationToken cancellationToken = default)
    {
        return await Context.FormSections.AnyAsync(cancellationToken)
            ? await Context.FormSections.MaxAsync(x => x.DisplayOrder, cancellationToken)
            : 0;
    }

    public async Task ReorderAsync(IReadOnlyList<Guid> orderedIds, CancellationToken cancellationToken = default)
    {
        var ids = orderedIds.Select(x => new FormSectionId(x)).ToList();
        var sections = await Context.FormSections.Where(x => ids.Contains(x.Id)).ToListAsync(cancellationToken);
        var orderById = orderedIds.Select((id, index) => (id, index)).ToDictionary(x => x.id, x => x.index);

        foreach (var section in sections)
        {
            if (orderById.TryGetValue(section.Id.Value, out var index))
            {
                section.SetDisplayOrder(index, dateTimeProvider.GetUtcNow());
            }
        }

        await Context.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> DeleteAsync(FormSectionId id, CancellationToken cancellationToken = default)
    {
        return await Context.FormSections.Where(x => x.Id == id).ExecuteDeleteAsync(cancellationToken);
    }
}