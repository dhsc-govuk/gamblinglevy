using Domain.DataCollectionForms;

namespace Application.Common.Interfaces.Repositories;

// BLUM-9102: add-only — a version is never updated or removed (a trigger refuses both in the database too).
public interface IFormModuleVersionRepository
{
    // Throws FormModuleVersionNumberTakenException when the version number is already taken for the module.
    Task<FormModuleVersion> AddAsync(FormModuleVersion version, CancellationToken cancellationToken = default);
}