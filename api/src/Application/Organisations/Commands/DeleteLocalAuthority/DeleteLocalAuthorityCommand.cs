using Application.Common.Funding;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Queries;
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
    IMaturityAssessmentQueries maturityAssessmentQueries,
    IAuditLogRepository auditLogRepository,
    ITransactionRunner transactionRunner,
    PermissionsService permissionsService,
    IDateTimeProvider dateTimeProvider)
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

        // Both submission flows: a started or submitted maturity assessment is as much a record as a quarterly report,
        // and its rows carry no FK to organisations, so deleting past it would orphan them silently (BLUM-7855).
        var hasSubmissions = await submissionProbe.HasAnyForOrganisationAsync(organisation.Id, cancellationToken)
            || await maturityAssessmentQueries.HasAnyForOrganisationAsync(organisation.Id, cancellationToken);
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

        // Every allocation the organisation holds is about to be removed, so the snapshot records both the year
        // in progress and the upcoming one — a single "latest" row would name whichever was written last, which
        // since BLUM-7608 is the upcoming year rather than the money the organisation is currently spending.
        var held = await allocationRepository.ListWithWindowsForOrganisationsAsync([organisation.Id], cancellationToken);
        var (currentAllocation, nextAllocation) = AllocationSelector.Select(held, dateTimeProvider.GetUtcNow());
        var beforeSnapshot = OrganisationSnapshot.Serialize(
            organisation,
            currentAllocation?.Allocation,
            currentAllocation?.Window,
            nextAllocation?.Allocation,
            nextAllocation?.Window);

        // The removals and the audit row commit together (BLUM-8748).
        return await transactionRunner.RunAsync<OrganisationException, Unit>(
            async token =>
            {
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
                        token);

                    await allocationRepository.RemoveByOrganisationAsync(organisation.Id, token);
                    await neighbourRepository.RemoveByOrganisationAsync(organisation.Id, token);
                    await organisationUserRepository.RemoveByOrganisationAsync(organisation.Id, token);
                    await organisationRepository.RemoveAsync(organisation, token);

                    return Unit.Default;
                }
                catch (Exception exception)
                {
                    return new OrganisationUnknownException(organisation.Id, exception);
                }
            },
            cancellationToken);
    }
}