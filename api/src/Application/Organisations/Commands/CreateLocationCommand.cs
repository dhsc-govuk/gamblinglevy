using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.Organisations.Audit;
using Application.Organisations.Exceptions;
using Domain.AuditLogs;
using Domain.Locations;
using Domain.Organisations;
using Domain.Users;
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
    ILocationRepository locationRepository,
    IAuditLogRepository auditLogRepository,
    ITransactionRunner transactionRunner)
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
                .BindAsync(_ => CreateLocation(p, organisationId, request.Name!, request.IsActive, cancellationToken))),
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
        Permission permission,
        OrganisationId organisationId,
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
                    var location = Location.New(
                        LocationId.New(),
                        organisationId,
                        name,
                        isActive);
                    var saved = await locationRepository.AddAsync(location, token);

                    await auditLogRepository.AppendAsync(
                        AuditLog.Create(
                            permissionsService.GetUserId().IfLeft(UserId.Empty()),
                            AuditTargetType.Location,
                            saved.Id.Value,
                            AuditAction.LocationCreated,
                            before: null,
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