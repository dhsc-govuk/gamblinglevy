using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Domain.DataCollectionForms;
using LanguageExt;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class FormModuleRepository(ApplicationDbContext dbContext)
    : RepositoryBase<FormModule, FormModuleId>(dbContext), IFormModuleRepository, IFormModuleQueries
{
    Task IFormModuleRepository.UpdateAsync(FormModule module, CancellationToken cancellationToken)
        => UpdateAsync(module, cancellationToken);

    public async Task<Option<FormModule>> GetByIdWithChildrenAsync(
        FormModuleId id,
        CancellationToken cancellationToken = default)
    {
        // Tracked load: command handlers add/remove sections, questions and options on this graph and rely
        // on the change tracker to insert/delete them on save.
        var entity = await Context.FormModules
            .Include(m => m.Sections)
            .Include(m => m.Questions)
            .ThenInclude(q => q.Options)
            .FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

        return entity is null ? Option<FormModule>.None : Option<FormModule>.Some(entity);
    }

    public async Task<IReadOnlyList<FormModule>> GetAllActiveAsync(CancellationToken cancellationToken = default)
    {
        return await Context.FormModules
            .AsNoTracking()
            .Where(m => m.IsActive)
            .OrderBy(m => m.DisplayOrder)
            .ToListAsync(cancellationToken);
    }

    public async Task<Option<FormModule>> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var entity = await Context.FormModules
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Code == code, cancellationToken);

        return entity is null ? Option<FormModule>.None : Option<FormModule>.Some(entity);
    }
}