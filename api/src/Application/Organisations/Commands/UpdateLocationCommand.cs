using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.Organisations.Exceptions;
using Domain.Locations;
using LanguageExt;
using MediatR;
using Location = Domain.Locations.Location;
using Unit = LanguageExt.Unit;

namespace Application.Organisations.Commands;

public record UpdateLocationCommand : IRequest<Either<LocationException, Location>>
{
    public Guid Id { get; init; }
    public string? Name { get; init; }
    public bool IsActive { get; init; }
}

public class UpdateLocationCommandHandler(
    ILocationRepository locationRepository,
    PermissionsService permissionsService)
    : IRequestHandler<UpdateLocationCommand, Either<LocationException, Location>>
{
    public async Task<Either<LocationException, Location>> Handle(
        UpdateLocationCommand request,
        CancellationToken cancellationToken)
    {
        var locationId = new LocationId(request.Id);

        var permissions = permissionsService.GetLocationPermissions();
        return await permissions.MatchAsync<Either<LocationException, Location>>(
            p => GetLocation(p, locationId, cancellationToken)
                .BindAsync(l => FindDuplicate(l, request.Name!, cancellationToken)
                .BindAsync(_ => UpdateLocation(l, request.Name!, request.IsActive, cancellationToken))),
            e => new LocationArgumentException(locationId, e.Message));
    }

    private async Task<Either<LocationException, Unit>> FindDuplicate(
        Location location,
        string name,
        CancellationToken cancellationToken)
    {
        var locationWithName = await locationRepository.FindDuplicateAsync(
            name,
            location.OrganisationId,
            location.Id,
            cancellationToken);
        return locationWithName.Match<Either<LocationException, Unit>>(
            l => new LocationAlreadyExistsException(l.Id, name),
            Unit.Default);
    }

    private async Task<Either<LocationException, Location>> UpdateLocation(
        Location location,
        string name,
        bool isActive,
        CancellationToken cancellationToken)
    {
        try
        {
            location.UpdateDetails(name, isActive);
            return await locationRepository.UpdateAsync(location, cancellationToken);
        }
        catch (Exception exception)
        {
            return new LocationUnknownException(LocationId.Empty(), exception);
        }
    }

    private async Task<Either<LocationException, Location>> GetLocation(
        Permission p,
        LocationId locationId,
        CancellationToken cancellationToken)
    {
        var location = await locationRepository.GetById(locationId, p, cancellationToken);
        return location.Match<Either<LocationException, Location>>(
            l => l,
            new LocationDoesNotExistException(locationId));
    }
}