using Domain.Forms;
using LanguageExt;

namespace Application.Common.Interfaces.Repositories;

public interface IFormSectionRepository
{
    Task<FormSection> AddAsync(FormSection entity, CancellationToken cancellationToken = default);
    Task<FormSection> UpdateAsync(FormSection entity, CancellationToken cancellationToken = default);
    Task<Option<FormSection>> GetByIdAsync(FormSectionId id, CancellationToken cancellationToken = default);

    // All sections ordered by display order (no tracking) — feeds the schema/list reads.
    Task<IReadOnlyList<FormSection>> GetAllAsync(CancellationToken cancellationToken = default);

    // Highest display order currently in use; the next section is placed after it. Returns 0 when empty.
    Task<int> GetMaxDisplayOrderAsync(CancellationToken cancellationToken = default);

    // Sets display order from the supplied ordered id list (index-based) in a single save.
    Task ReorderAsync(IReadOnlyList<Guid> orderedIds, CancellationToken cancellationToken = default);

    Task<int> DeleteAsync(FormSectionId id, CancellationToken cancellationToken = default);
}