using Application.Common.Interfaces.Queries;
using Domain.Categories;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class CategoryRepository(ApplicationDbContext dbContext)
    : RepositoryBase<Category, CategoryId>(dbContext), ICategoryQueries
{
    public async Task<IReadOnlyList<Category>> GetActiveCategoriesAsync(CancellationToken cancellationToken = default)
    {
        return await Context.Categories
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.SequenceOrder)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ActivityTask>> GetActiveTasksAsync(CancellationToken cancellationToken = default)
    {
        return await Context.ActivityTasks
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.SequenceOrder)
            .ToListAsync(cancellationToken);
    }
}