using Application.Common.Interfaces;
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

namespace Application.Organisations.Commands.SuspendLocalAuthority;

public record SuspendLocalAuthorityCommand(Guid Id) : IRequest<Either<OrganisationException, Organisation>>;

public class SuspendLocalAuthorityCommandHandler(
    IOrganisationRepository organisationRepository,
    IOrganisationUserRepository organisationUserRepository,
    IMfaSessionService mfaSessionService,
    IUserClaimsCache userClaimsCache,
    IAuditLogRepository auditLogRepository,
    PermissionsService permissionsService)
    : IRequestHandler<SuspendLocalAuthorityCommand, Either<OrganisationException, Organisation>>
{
    public async Task<Either<OrganisationException, Organisation>> Handle(
        SuspendLocalAuthorityCommand request,
        CancellationToken cancellationToken)
    {
        var actorResult = permissionsService.GetUserId();
        return await actorResult.MatchAsync<Either<OrganisationException, Organisation>>(
            actorId => HandleWithActor(request, actorId, cancellationToken),
            e => new OrganisationUnknownException(new OrganisationId(request.Id), e));
    }

    private async Task<Either<OrganisationException, Organisation>> HandleWithActor(
        SuspendLocalAuthorityCommand request,
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

        if (status != OnboardingStatuses.Active && status != OnboardingStatuses.Invited)
        {
            return new OrganisationCannotBeSuspendedException(organisation.Id, status);
        }

        var before = OrganisationSnapshot.Serialize(organisation);

        try
        {
            organisation.MarkSuspended();
            var saved = await organisationRepository.UpdateAsync(organisation, cancellationToken);

            // Immediately block every user of this organisation: revoke MFA sessions and invalidate the
            // cached claims (5-min TTL) so the next request re-reads the now-suspended organisation status.
            var activeUsers = await organisationUserRepository.GetActiveUsersAsync(saved.Id, cancellationToken);
            foreach (var user in activeUsers)
            {
                await mfaSessionService.RevokeAllUserSessionsAsync(user.Id);
                userClaimsCache.Invalidate(user.SubId);
            }

            await auditLogRepository.AppendAsync(
                AuditLog.Create(
                    actorId,
                    AuditTargetType.Organisation,
                    saved.Id.Value,
                    AuditAction.OrganisationSuspended,
                    before,
                    OrganisationSnapshot.Serialize(saved),
                    organisationId: saved.Id),
                cancellationToken);

            return saved;
        }
        catch (Exception exception)
        {
            return new OrganisationUnknownException(organisation.Id, exception);
        }
    }
}