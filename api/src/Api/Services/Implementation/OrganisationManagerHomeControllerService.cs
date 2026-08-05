using Api.Dtos;
using Api.Services.Abstract;
using Application.Common.Interfaces.Queries;
using Application.Common.Permissions;
using Domain.Organisations;
using LanguageExt;

namespace Api.Services.Implementation;

public class OrganisationManagerHomeControllerService(
    IOrganisationUserQueries organisationUserQueries,
    ILocationQueries locationQueries) : IOrganisationManagerHomeControllerService
{
    public async Task<OrganisationAdminHomeDto> GetTotals(Permission permission, CancellationToken cancellationToken)
    {
        return new OrganisationAdminHomeDto(
            Admins: await organisationUserQueries.Count(Option<OrganisationId>.None, permission, cancellationToken),
            Locations: await locationQueries.Count(Option<OrganisationId>.None, permission, cancellationToken));
    }
}