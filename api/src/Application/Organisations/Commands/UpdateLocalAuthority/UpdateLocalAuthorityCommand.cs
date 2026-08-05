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

namespace Application.Organisations.Commands.UpdateLocalAuthority;

public record UpdateLocalAuthorityCommand : IRequest<Either<OrganisationException, UpdateLocalAuthorityResult>>
{
    public Guid Id { get; init; }
    public string? Name { get; init; }
    public string? OnsCode { get; init; }
    public string? Region { get; init; }
    public string? Type { get; init; }
    public string? ECode { get; init; }
    public string? OnboardingStatus { get; init; }
    public bool ConfirmOnsCodeChange { get; init; }
}

public record UpdateLocalAuthorityResult(Organisation Organisation, OrgFundingAllocation? CurrentAllocation);

public class UpdateLocalAuthorityCommandHandler(
    IOrganisationRepository organisationRepository,
    IOrgFundingAllocationRepository allocationRepository,
    IAuditLogRepository auditLogRepository,
    PermissionsService permissionsService)
    : IRequestHandler<UpdateLocalAuthorityCommand, Either<OrganisationException, UpdateLocalAuthorityResult>>
{
    public async Task<Either<OrganisationException, UpdateLocalAuthorityResult>> Handle(
        UpdateLocalAuthorityCommand request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.OnboardingStatus))
        {
            return new OrganisationOnboardingStatusNotEditableException(new OrganisationId(request.Id));
        }

        var actorResult = permissionsService.GetUserId();
        return await actorResult.MatchAsync<Either<OrganisationException, UpdateLocalAuthorityResult>>(
            actorId => HandleWithActor(request, actorId, cancellationToken),
            e => new OrganisationUnknownException(new OrganisationId(request.Id), e));
    }

    private async Task<Either<OrganisationException, UpdateLocalAuthorityResult>> HandleWithActor(
        UpdateLocalAuthorityCommand request,
        UserId actorId,
        CancellationToken cancellationToken)
    {
        var organisationId = new OrganisationId(request.Id);
        var lookup = await organisationRepository.GetOrganisationById(organisationId, cancellationToken);

        if (lookup.IsNone)
        {
            return new OrganisationDoesNotExistException(organisationId);
        }

        return await Apply(lookup.ValueUnsafe(), request, actorId, cancellationToken);
    }

    private async Task<Either<OrganisationException, UpdateLocalAuthorityResult>> Apply(
        Organisation organisation,
        UpdateLocalAuthorityCommand request,
        UserId actorId,
        CancellationToken cancellationToken)
    {
        var newName = request.Name!.Trim();
        var newOnsCode = string.IsNullOrWhiteSpace(request.OnsCode) ? null : request.OnsCode!.Trim();
        // Non-null by validator, which rejects an empty E-code before the handler runs.
        var newECode = request.ECode!.Trim();

        if (!string.Equals(organisation.OnsCode, newOnsCode, StringComparison.Ordinal) && !request.ConfirmOnsCodeChange)
        {
            return new OrganisationOnsCodeChangeNotConfirmedException(organisation.Id);
        }

        if (!string.Equals(organisation.Name, newName, StringComparison.Ordinal))
        {
            var duplicateName = await organisationRepository.FindDuplicateAsync(newName, organisation.Id, cancellationToken);
            if (duplicateName.IsSome)
            {
                return duplicateName.Match<OrganisationException>(
                    o => new OrganisationAlreadyExistsException(o.Id, newName),
                    () => new OrganisationUnknownException(organisation.Id, new Exception("unreachable")));
            }
        }

        if (newOnsCode is not null && !string.Equals(organisation.OnsCode, newOnsCode, StringComparison.Ordinal))
        {
            var duplicateOns = await organisationRepository.FindDuplicateOnsCodeAsync(newOnsCode, organisation.Id, cancellationToken);
            if (duplicateOns.IsSome)
            {
                return duplicateOns.Match<OrganisationException>(
                    o => new OrganisationDuplicateOnsCodeException(o.Id, newOnsCode),
                    () => new OrganisationUnknownException(organisation.Id, new Exception("unreachable")));
            }
        }

        var currentAllocation = await allocationRepository.FindLatestForOrganisationAsync(organisation.Id, cancellationToken);
        var beforeAllocation = currentAllocation.IsSome ? currentAllocation.ValueUnsafe() : null;
        var before = OrganisationSnapshot.Serialize(organisation, beforeAllocation);

        try
        {
            organisation.UpdateDetails(
                newName,
                Region.From(request.Region!),
                OrgType.From(request.Type!),
                newECode,
                newOnsCode);

            var saved = await organisationRepository.UpdateAsync(organisation, cancellationToken);

            await auditLogRepository.AppendAsync(
                AuditLog.Create(
                    actorId,
                    AuditTargetType.Organisation,
                    saved.Id.Value,
                    AuditAction.OrganisationUpdated,
                    before,
                    OrganisationSnapshot.Serialize(saved, beforeAllocation),
                    organisationId: saved.Id),
                cancellationToken);

            return new UpdateLocalAuthorityResult(saved, beforeAllocation);
        }
        catch (Exception exception)
        {
            return new OrganisationUnknownException(organisation.Id, exception);
        }
    }
}