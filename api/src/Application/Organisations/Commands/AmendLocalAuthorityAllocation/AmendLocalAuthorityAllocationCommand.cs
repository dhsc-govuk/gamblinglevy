using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.Organisations.Audit;
using Application.Organisations.Exceptions;
using Domain.AuditLogs;
using Domain.Funding;
using Domain.Organisations;
using Domain.Users;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using MediatR;

namespace Application.Organisations.Commands.AmendLocalAuthorityAllocation;

public record AmendLocalAuthorityAllocationCommand : IRequest<Either<OrganisationException, AmendLocalAuthorityAllocationResult>>
{
    public Guid Id { get; init; }
    public decimal AllocatedAmount { get; init; }
}

public record AmendLocalAuthorityAllocationResult(Organisation Organisation, OrgFundingAllocation Allocation);

public class AmendLocalAuthorityAllocationCommandHandler(
    IOrganisationRepository organisationRepository,
    IOrgFundingAllocationRepository allocationRepository,
    IAuditLogRepository auditLogRepository,
    PermissionsService permissionsService)
    : IRequestHandler<AmendLocalAuthorityAllocationCommand, Either<OrganisationException, AmendLocalAuthorityAllocationResult>>
{
    public async Task<Either<OrganisationException, AmendLocalAuthorityAllocationResult>> Handle(
        AmendLocalAuthorityAllocationCommand request,
        CancellationToken cancellationToken)
    {
        var actorResult = permissionsService.GetUserId();
        return await actorResult.MatchAsync<Either<OrganisationException, AmendLocalAuthorityAllocationResult>>(
            actorId => HandleWithActor(request, actorId, cancellationToken),
            e => new OrganisationUnknownException(new OrganisationId(request.Id), e));
    }

    private async Task<Either<OrganisationException, AmendLocalAuthorityAllocationResult>> HandleWithActor(
        AmendLocalAuthorityAllocationCommand request,
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

        var allocationLookup = await allocationRepository.FindLatestForOrganisationAsync(organisation.Id, cancellationToken);
        if (allocationLookup.IsNone)
        {
            return new OrganisationAllocationNotFoundException(organisation.Id);
        }

        var allocation = allocationLookup.ValueUnsafe();
        var before = OrganisationSnapshot.Serialize(organisation, allocation);

        try
        {
            allocation.UpdateAmount(request.AllocatedAmount, actorId);
            var saved = await allocationRepository.UpdateAsync(allocation, cancellationToken);

            await auditLogRepository.AppendAsync(
                AuditLog.Create(
                    actorId,
                    AuditTargetType.Organisation,
                    organisation.Id.Value,
                    AuditAction.OrganisationAllocationAmended,
                    before,
                    OrganisationSnapshot.Serialize(organisation, saved),
                    organisationId: organisation.Id),
                cancellationToken);

            return new AmendLocalAuthorityAllocationResult(organisation, saved);
        }
        catch (Exception exception)
        {
            return new OrganisationUnknownException(organisation.Id, exception);
        }
    }
}