using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.Organisations.Audit;
using Application.Organisations.Exceptions;
using Domain.AuditLogs;
using Domain.Locations;
using Domain.Users;
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
    PermissionsService permissionsService,
    IAuditLogRepository auditLogRepository,
    ITransactionRunner transactionRunner)
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
                .BindAsync(_ => UpdateLocation(p, l, request.Name!, request.IsActive, cancellationToken))),
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
        Permission permission,
        Location location,
        string name,
        bool isActive,
        CancellationToken cancellationToken)
    {
        // The change and its audit row commit together (BLUM-8748).
        return await transactionRunner.RunAsync<LocationException, Location>(
            async token =>
            {
                try
                {
                    var before = LocationSnapshot.Serialize(location);
                    location.UpdateDetails(name, isActive);
                    var saved = await locationRepository.UpdateAsync(location, token);

                    await auditLogRepository.AppendAsync(
                        AuditLog.Create(
                            permissionsService.GetUserId().IfLeft(UserId.Empty()),
                            AuditTargetType.Location,
                            saved.Id.Value,
                            AuditAction.LocationUpdated,
                            before,
                            after: LocationSnapshot.Serialize(saved),
                            saved.OrganisationId),
                        token);

                    return saved;
                }
                catch (Exception exception)
                {
                    return new LocationUnknownException(LocationId.Empty(), exception);
                }
            },
            cancellationToken);
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