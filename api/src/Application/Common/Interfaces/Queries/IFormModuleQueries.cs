using Domain.DataCollectionForms;
using LanguageExt;

namespace Application.Common.Interfaces.Queries;

public interface IFormModuleQueries
{
    // Loads a module with its sections and questions (and question options) eagerly — the form-builder edit/preview
    // surface needs the full structure.
    Task<Option<FormModule>> GetByIdWithChildrenAsync(FormModuleId id, CancellationToken cancellationToken = default);

    // Active modules — feeds the module list and the collection form-assignment picker.
    Task<IReadOnlyList<FormModule>> GetAllActiveAsync(CancellationToken cancellationToken = default);

    // Code uniqueness guard / lookup. Code is the stable module identifier.
    Task<Option<FormModule>> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
}