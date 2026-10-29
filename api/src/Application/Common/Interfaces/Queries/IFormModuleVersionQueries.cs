using Domain.DataCollectionForms;
using LanguageExt;

namespace Application.Common.Interfaces.Queries;

public interface IFormModuleVersionQueries
{
    // The module's highest-numbered version, or None if it has never been published with versioning on.
    Task<Option<FormModuleVersion>> GetLatestAsync(FormModuleId moduleId, CancellationToken cancellationToken = default);

    // BLUM-9103: the latest version of each of these modules. A module with no versions (or a maturity catalogue id,
    // which is not a module) is simply absent from the result.
    Task<IReadOnlyDictionary<FormModuleId, FormModuleVersionId>> GetLatestVersionIdsAsync(
        IReadOnlyCollection<FormModuleId> moduleIds,
        CancellationToken cancellationToken = default);

    // BLUM-9103: the version number behind each of these version ids, for showing what a collection records.
    Task<IReadOnlyDictionary<FormModuleVersionId, int>> GetVersionNumbersAsync(
        IReadOnlyCollection<FormModuleVersionId> versionIds,
        CancellationToken cancellationToken = default);

    // Every version of the module, newest first.
    Task<IReadOnlyList<FormModuleVersion>> ListAsync(FormModuleId moduleId, CancellationToken cancellationToken = default);

    // BLUM-9086: the id and number of every version of the module, newest first, without the saved definitions.
    Task<IReadOnlyList<FormModuleVersionNumberRow>> ListNumbersAsync(FormModuleId moduleId, CancellationToken cancellationToken = default);

    Task<Option<FormModuleVersion>> GetAsync(FormModuleId moduleId, int versionNumber, CancellationToken cancellationToken = default);

    // BLUM-9104: the version a collection records, by its id.
    Task<Option<FormModuleVersion>> GetByIdAsync(FormModuleVersionId versionId, CancellationToken cancellationToken = default);
}

public readonly record struct FormModuleVersionNumberRow(FormModuleVersionId Id, int VersionNumber);