using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.Organisations.Audit;
using Application.Organisations.Exceptions;
using Domain.AuditLogs;
using Domain.Organisations;
using Domain.Users;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using MediatR;
using Unit = LanguageExt.Unit;

namespace Application.Organisations.Commands.DeleteLocalAuthority;

public record DeleteLocalAuthorityCommand : IRequest<Either<OrganisationException, Unit>>
{
    public Guid Id { get; init; }
    public bool AcknowledgeActiveUsers { get; init; }
}

public class DeleteLocalAuthorityCommandHandler(
    IOrganisationRepository organisationRepository,
    IOrganisationUserRepository organisationUserRepository,
    IOrgFundingAllocationRepository allocationRepository,
    IOrgNeighbourRepository neighbourRepository,
    ISubmissionProbe submissionProbe,
    IAuditLogRepository auditLogRepository,
    PermissionsService permissionsService)
    : IRequestHandler<DeleteLocalAuthorityCommand, Either<OrganisationException, Unit>>
{
    public async Task<Either<OrganisationException, Unit>> Handle(
        DeleteLocalAuthorityCommand request,
        CancellationToken cancellationToken)
    {
        var actorResult = permissionsService.GetUserId();
        return await actorResult.MatchAsync<Either<OrganisationException, Unit>>(
            actorId => HandleWithActor(request, actorId, cancellationToken),
            e => new OrganisationUnknownException(new OrganisationId(request.Id), e));
    }

    private async Task<Either<OrganisationException, Unit>> HandleWithActor(
        DeleteLocalAuthorityCommand request,
        UserId actorId,
        CancellationToken cancellationToken)
    {
        var organisationId = new OrganisationId(request.Id);
        var lookup = await organisationRepository.GetOrganisationById(organisationId, cancellationToken);

        if (lookup.IsNone)
        {
            return new OrganisationDoesNotExistException(organisationId);
        }

        var organisation = lookup.ValueUnsafe();
        var status = organisation.OnboardingStatus.ToString();

        if (status != OnboardingStatuses.Invited && status != OnboardingStatuses.Suspended)
        {
            return new OrganisationCannotBeDeletedException(organisation.Id, status);
        }

        var hasSubmissions = await submissionProbe.HasAnyForOrganisationAsync(organisation.Id, cancellationToken);
        if (hasSubmissions)
        {
            return new OrganisationHasSubmissionsException(organisation.Id);
        }

        if (!request.AcknowledgeActiveUsers)
        {
            var activeUsers = await organisationUserRepository.GetActiveUsersAsync(organisation.Id, cancellationToken);
            if (activeUsers.Count > 0)
            {
                return new OrganisationHasActiveUsersException(organisation.Id, activeUsers);
            }
        }

        var allocation = await allocationRepository.FindLatestForOrganisationAsync(organisation.Id, cancellationToken);
        var beforeAllocation = allocation.IsSome ? allocation.ValueUnsafe() : null;
        var beforeSnapshot = OrganisationSnapshot.Serialize(organisation, beforeAllocation);

        try
        {
            await auditLogRepository.AppendAsync(
                AuditLog.Create(
                    actorId,
                    AuditTargetType.Organisation,
                    organisation.Id.Value,
                    AuditAction.OrganisationDeleted,
                    before: beforeSnapshot,
                    after: null,
                    organisationId: organisation.Id),
                cancellationToken);

            await allocationRepository.RemoveByOrganisationAsync(organisation.Id, cancellationToken);
            await neighbourRepository.RemoveByOrganisationAsync(organisation.Id, cancellationToken);
            await organisationUserRepository.RemoveByOrganisationAsync(organisation.Id, cancellationToken);
            await organisationRepository.RemoveAsync(organisation, cancellationToken);

            return Unit.Default;
        }
        catch (Exception exception)
        {
            return new OrganisationUnknownException(organisation.Id, exception);
        }
    }
}