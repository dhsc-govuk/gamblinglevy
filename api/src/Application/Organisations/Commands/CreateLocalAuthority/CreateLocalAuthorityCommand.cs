using Application.Common.Funding;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.Organisations.Audit;
using Application.Organisations.Exceptions;
using Domain.AuditLogs;
using Domain.Funding;
using Domain.Organisations;
using Domain.Users;
using LanguageExt;
using MediatR;

namespace Application.Organisations.Commands.CreateLocalAuthority;

public record CreateLocalAuthorityCommand : IRequest<Either<OrganisationException, CreateLocalAuthorityResult>>
{
    public string? Name { get; init; }
    public string? OnsCode { get; init; }
    public string? Region { get; init; }
    public string? Type { get; init; }
    public string? ECode { get; init; }
    public decimal AllocatedAmount { get; init; }
}

public record CreateLocalAuthorityResult(Organisation Organisation, OrgFundingAllocation Allocation);

public class CreateLocalAuthorityCommandHandler(
    IOrganisationRepository organisationRepository,
    IOrgFundingAllocationRepository allocationRepository,
    IFundingWindowResolver fundingWindowResolver,
    IAuditLogRepository auditLogRepository,
    PermissionsService permissionsService)
    : IRequestHandler<CreateLocalAuthorityCommand, Either<OrganisationException, CreateLocalAuthorityResult>>
{
    public async Task<Either<OrganisationException, CreateLocalAuthorityResult>> Handle(
        CreateLocalAuthorityCommand request,
        CancellationToken cancellationToken)
    {
        var actorResult = permissionsService.GetUserId();
        return await actorResult.MatchAsync<Either<OrganisationException, CreateLocalAuthorityResult>>(
            actorId => HandleWithActor(request, actorId, cancellationToken),
            e => new OrganisationUnknownException(OrganisationId.Empty(), e));
    }

    private async Task<Either<OrganisationException, CreateLocalAuthorityResult>> HandleWithActor(
        CreateLocalAuthorityCommand request,
        UserId actorId,
        CancellationToken cancellationToken)
    {
        var name = request.Name!.Trim();
        // Null for a VCSE organisation, which has no ONS code; the validator guarantees a value for an LA.
        var onsCode = string.IsNullOrWhiteSpace(request.OnsCode) ? null : request.OnsCode!.Trim();

        var duplicateName = await organisationRepository.FindByNameAsync(name, cancellationToken);
        if (duplicateName.IsSome)
        {
            return duplicateName.Match<OrganisationException>(
                o => new OrganisationAlreadyExistsException(o.Id, name),
                () => new OrganisationUnknownException(OrganisationId.Empty(), new Exception("unreachable")));
        }

        if (onsCode is not null)
        {
            var duplicateOns = await organisationRepository.FindByOnsCodeAsync(onsCode, cancellationToken);
            if (duplicateOns.IsSome)
            {
                return duplicateOns.Match<OrganisationException>(
                    o => new OrganisationDuplicateOnsCodeException(o.Id, onsCode),
                    () => new OrganisationUnknownException(OrganisationId.Empty(), new Exception("unreachable")));
            }
        }

        try
        {
            var organisation = Organisation.New(
                OrganisationId.New(),
                name,
                onsCode,
                Region.From(request.Region!),
                OrgType.From(request.Type!),
                // Non-null by validator: CreateLocalAuthorityCommandValidator rejects an empty E-code before
                // the handler runs, so there is no "no code supplied" case to fold into null here.
                request.ECode!.Trim(),
                OnboardingStatus.Invited);

            var savedOrg = await organisationRepository.AddAsync(organisation, cancellationToken);

            var window = await fundingWindowResolver.ResolveCurrentAsync(cancellationToken);

            var allocation = OrgFundingAllocation.New(
                OrgFundingAllocationId.New(),
                savedOrg.Id,
                window.Id,
                request.AllocatedAmount,
                actorId);

            var savedAllocation = await allocationRepository.AddAsync(allocation, cancellationToken);

            await auditLogRepository.AppendAsync(
                AuditLog.Create(
                    actorId,
                    AuditTargetType.Organisation,
                    savedOrg.Id.Value,
                    AuditAction.OrganisationCreated,
                    before: null,
                    after: OrganisationSnapshot.Serialize(savedOrg, savedAllocation),
                    organisationId: savedOrg.Id),
                cancellationToken);

            return new CreateLocalAuthorityResult(savedOrg, savedAllocation);
        }
        catch (Exception exception)
        {
            return new OrganisationUnknownException(OrganisationId.Empty(), exception);
        }
    }
}