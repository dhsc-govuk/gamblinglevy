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

namespace Application.Organisations.Commands.ReinstateLocalAuthority;

public record ReinstateLocalAuthorityCommand(Guid Id) : IRequest<Either<OrganisationException, Organisation>>;

public class ReinstateLocalAuthorityCommandHandler(
    IOrganisationRepository organisationRepository,
    IOrganisationUserRepository organisationUserRepository,
    IUserClaimsCache userClaimsCache,
    IAuditLogRepository auditLogRepository,
    PermissionsService permissionsService)
    : IRequestHandler<ReinstateLocalAuthorityCommand, Either<OrganisationException, Organisation>>
{
    public async Task<Either<OrganisationException, Organisation>> Handle(
        ReinstateLocalAuthorityCommand request,
        CancellationToken cancellationToken)
    {
        var actorResult = permissionsService.GetUserId();
        return await actorResult.MatchAsync<Either<OrganisationException, Organisation>>(
            actorId => HandleWithActor(request, actorId, cancellationToken),
            e => new OrganisationUnknownException(new OrganisationId(request.Id), e));
    }

    private async Task<Either<OrganisationException, Organisation>> HandleWithActor(
        ReinstateLocalAuthorityCommand request,
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

        if (status != OnboardingStatuses.Suspended)
        {
            return new OrganisationCannotBeReinstatedException(organisation.Id, status);
        }

        var before = OrganisationSnapshot.Serialize(organisation);

        try
        {
            organisation.MarkActive();
            var saved = await organisationRepository.UpdateAsync(organisation, cancellationToken);

            // Clear the cached (suspended) claims so users regain access on their next request. MFA sessions
            // are intentionally NOT recreated here — they were revoked on suspend, forcing a fresh sign-in.
            var activeUsers = await organisationUserRepository.GetActiveUsersAsync(saved.Id, cancellationToken);
            foreach (var user in activeUsers)
            {
                userClaimsCache.Invalidate(user.SubId);
            }

            await auditLogRepository.AppendAsync(
                AuditLog.Create(
                    actorId,
                    AuditTargetType.Organisation,
                    saved.Id.Value,
                    AuditAction.OrganisationReinstated,
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