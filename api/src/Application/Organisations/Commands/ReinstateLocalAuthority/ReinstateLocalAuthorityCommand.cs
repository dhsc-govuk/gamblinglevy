using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.Organisations.Audit;
using Application.Organisations.Exceptions;
using Application.Users;
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
    ITransactionRunner transactionRunner,
    UserInvitations userInvitations,
    PermissionsService permissionsService,
    IDateTimeProvider dateTimeProvider)
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

        // The status change and its audit row commit together (BLUM-8748); the claims and invitations follow the
        // commit, so a rollback leaves no one let back in and no invitation sent.
        var result = await transactionRunner.RunAsync<OrganisationException, ReinstateOutcome>(
            async token =>
            {
                try
                {
                    organisation.MarkActive(dateTimeProvider.GetUtcNow());
                    var saved = await organisationRepository.UpdateAsync(organisation, token);

                    // Only users who are still active. Suspending deactivates every active user (BLUM-7667), and
                    // they stay deactivated here on purpose (AC4) — each is reactivated individually. What is left is
                    // anyone added or reactivated while the organisation was suspended, and anyone from an
                    // organisation suspended before the cascade existed.
                    var activeUsers = await organisationUserRepository.GetActiveUsersAsync(saved.Id, token);

                    await auditLogRepository.AppendAsync(
                        AuditLog.Create(
                            actorId,
                            AuditTargetType.Organisation,
                            saved.Id.Value,
                            AuditAction.OrganisationReinstated,
                            before,
                            OrganisationSnapshot.Serialize(saved),
                            organisationId: saved.Id),
                        token);

                    return new ReinstateOutcome(saved, activeUsers);
                }
                catch (Exception exception)
                {
                    return new OrganisationUnknownException(organisation.Id, exception);
                }
            },
            cancellationToken);

        return await result.MatchAsync<Either<OrganisationException, Organisation>>(
            async committed =>
            {
                try
                {
                    // Clear the cached (suspended) claims so users regain access on their next request. MFA sessions
                    // are intentionally NOT recreated here — they were revoked on suspend, forcing a fresh sign-in.
                    foreach (var user in committed.ActiveUsers)
                    {
                        userClaimsCache.Invalidate(user.SubId);
                    }

                    // A user reactivated, or added, while the organisation was still deactivated had their invitation
                    // held, because they could not sign in yet (BLUM-7945). Now they can, so it goes out. Only active
                    // users are passed: anyone still deactivated keeps theirs until they are reactivated individually.
                    await userInvitations.SendHeldOnOrganisationReactivationAsync(committed.ActiveUsers);

                    return committed.Saved;
                }
                catch (Exception exception)
                {
                    return new OrganisationUnknownException(organisation.Id, exception);
                }
            },
            exception => exception);
    }

    private sealed record ReinstateOutcome(Organisation Saved, IReadOnlyList<User> ActiveUsers);
}