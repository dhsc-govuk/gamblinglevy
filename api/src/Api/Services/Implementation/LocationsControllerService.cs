using Api.Dtos;
using Api.Services.Abstract;
using Application.Common.Interfaces.Queries;
using Application.Common.Permissions;
using Domain.Locations;
using Domain.Organisations;
using LanguageExt;

namespace Api.Services.Implementation;

public class LocationsControllerService(
    ILocationQueries locationQueries,
    ILocationUserQueries locationUserQueries) : ILocationsControllerService
{
    public async Task<IEnumerable<LocationDto>> GetAll(
        Permission permission,
        Guid organisationId,
        CancellationToken cancellationToken)
    {
        var locations = await locationQueries.GetAll(new OrganisationId(organisationId), permission, cancellationToken);
        return locations.Select(x => new LocationDto(
            x.Id.Value,
            x.OrganisationId.Value,
            x.Name,
            x.IsActive));
    }

    public async Task<Option<LocationDto>> Get(
        Permission permission,
        Guid locationId,
        CancellationToken cancellationToken)
    {
        var location = await locationQueries.GetById(new LocationId(locationId), permission, cancellationToken);
        return location.Match(
            l => new LocationDto(
                l.Id.Value,
                l.OrganisationId.Value,
                l.Name,
                l.IsActive,
                OrganisationDto.FromDomainModel(l.Organisation!)),
            () => Option<LocationDto>.None);
    }

    public async Task<Option<LocationHomeDto>> Totals(
        Permission permission,
        Guid locationId,
        CancellationToken cancellationToken)
    {
        var locId = new LocationId(locationId);

        var location = await locationQueries.GetById(locId, permission, cancellationToken);
        var admins = await locationUserQueries.Count(locId, permission, cancellationToken);

        return location.Match(
            l => new LocationHomeDto(
                LocationName: l.Name,
                OrganisationName: l.Organisation!.Name,
                Admins: admins),
            () => Option<LocationHomeDto>.None);
    }
}