using Api.Dtos;
using Domain.DataCollectionForms;
using LanguageExt;

namespace Api.Services.Abstract;

public interface IDataCollectionFormsControllerService
{
    // Projects a module aggregate (with its owned sections / questions / options) to the authoring DTO.
    FormModuleWithChildrenDto ToDto(FormModule module);

    // Active forms-builder modules — feeds the module list on the DHSC authoring surface.
    Task<IReadOnlyList<FormModuleDto>> ListAsync(CancellationToken cancellationToken);

    // BLUM-9102: a module's saved versions, newest first, with who published each.
    Task<IReadOnlyList<FormModuleVersionSummaryDto>> ListVersionsAsync(FormModuleId moduleId, CancellationToken cancellationToken);

    // BLUM-9086: each of the module's saved versions, newest first, with the non-deleted collections that record it.
    Task<IReadOnlyList<FormModuleVersionCollectionsDto>> ListVersionCollectionsAsync(FormModuleId moduleId, CancellationToken cancellationToken);

    // BLUM-9102: one saved version with its full content. None when the module has no such version.
    Task<Option<FormModuleVersionDto>> GetVersionAsync(FormModuleId moduleId, int versionNumber, CancellationToken cancellationToken);
}