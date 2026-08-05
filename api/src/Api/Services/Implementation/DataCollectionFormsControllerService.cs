using Api.Dtos;
using Api.Services.Abstract;
using Application.Common.Interfaces.Queries;

namespace Api.Services.Implementation;

public class DataCollectionFormsControllerService(IFormModuleQueries formModuleQueries)
    : IDataCollectionFormsControllerService
{
    public FormModuleWithChildrenDto ToDto(Domain.DataCollectionForms.FormModule module) =>
        FormModuleWithChildrenDto.FromDomainModel(module);

    public async Task<IReadOnlyList<FormModuleDto>> ListAsync(CancellationToken cancellationToken)
    {
        var modules = await formModuleQueries.GetAllActiveAsync(cancellationToken);
        return modules
            .OrderBy(m => m.DisplayOrder)
            .Select(FormModuleDto.FromDomainModel)
            .ToList();
    }
}