using Domain.DataCollectionForms;

namespace Application.Common.Interfaces.Repositories;

public interface IFormModuleRepository
{
    Task<FormModule> AddAsync(FormModule module, CancellationToken cancellationToken = default);
    Task UpdateAsync(FormModule module, CancellationToken cancellationToken = default);
}