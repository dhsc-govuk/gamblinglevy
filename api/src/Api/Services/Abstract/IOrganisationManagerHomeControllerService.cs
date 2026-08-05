using Api.Dtos;
using Application.Common.Permissions;

namespace Api.Services.Abstract;

public interface IOrganisationManagerHomeControllerService
{
    Task<OrganisationAdminHomeDto> GetTotals(Permission permission, CancellationToken cancellationToken);
}