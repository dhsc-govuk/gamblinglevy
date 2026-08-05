using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.Organisations.Exceptions;
using Domain.Locations;
using Domain.Organisations;
using LanguageExt;
using MediatR;
using Location = Domain.Locations.Location;
using Unit = LanguageExt.Unit;

namespace Application.Organisations.Commands;

public record CreateLocationCommand : IRequest<Either<LocationException, Location>>
{
    public Guid OrganisationId { get; init; }
    public string? Name { get; init; }
    public bool IsActive { get; init; }
}

public class CreateLocationCommandHandler(
    PermissionsService permissionsService,
    IOrganisationRepository organisationRepository,
    ILocationRepository locationRepository)
    : IRequestHandler<CreateLocationCommand, Either<LocationException, Location>>
{
    public Task<Either<LocationException, Location>> Handle(
        CreateLocationCommand request,
        CancellationToken cancellationToken)
    {
        var organisationId = new OrganisationId(request.OrganisationId);

        var permissions = permissionsService.GetOrganisationPermissions();
        return permissions.MatchAsync(
            p => GetOrganisation(p, organisationId, cancellationToken)
                .BindAsync(_ => CheckIfExist(request.Name!, organisationId, cancellationToken)
                .BindAsync(_ => CreateLocation(organisationId, request.Name!, request.IsActive, cancellationToken))),
            e => new LocationArgumentException(LocationId.Empty(), e.Message));
    }

    private async Task<Either<LocationException, Unit>> CheckIfExist(
        string name,
        OrganisationId organisationId,
        CancellationToken cancellationToken)
    {
        var locationWithName = await locationRepository.FindByNameAsync(
            name,
            organisationId,
            cancellationToken);

        return locationWithName.Match<Either<LocationException, Unit>>(
            l => new LocationAlreadyExistsException(l.Id, name),
            Unit.Default);
    }

    private async Task<Either<LocationException, Location>> CreateLocation(
        OrganisationId organisationId,
        string name,
        bool isActive,
        CancellationToken cancellationToken)
    {
        try
        {
            var location = Location.New(
                LocationId.New(),
                organisationId,
                name,
                isActive);
            return await locationRepository.AddAsync(location, cancellationToken);
        }
        catch (Exception exception)
        {
            return new LocationUnknownException(LocationId.Empty(), exception);
        }
    }

    private async Task<Either<LocationException, Unit>> GetOrganisation(
        Permission p,
        OrganisationId organisationId,
        CancellationToken cancellationToken)
    {
        var organisation = await organisationRepository.GetOrganisationById(organisationId, p, cancellationToken);
        return organisation.Match<Either<LocationException, Unit>>(
            _ => Unit.Default,
            new LocationOrganisationDoesNotExistException(LocationId.Empty(), organisationId));
    }
}