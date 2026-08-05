using Api.Dtos;
using Domain.DataCollectionForms;

namespace Api.Services.Abstract;

public interface IDataCollectionFormsControllerService
{
    // Projects a module aggregate (with its owned sections / questions / options) to the authoring DTO.
    FormModuleWithChildrenDto ToDto(FormModule module);

    // Active forms-builder modules — feeds the module list on the DHSC authoring surface.
    Task<IReadOnlyList<FormModuleDto>> ListAsync(CancellationToken cancellationToken);
}