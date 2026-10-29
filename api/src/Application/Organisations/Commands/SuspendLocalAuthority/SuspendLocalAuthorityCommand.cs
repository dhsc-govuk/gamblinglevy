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

namespace Application.Organisations.Commands.SuspendLocalAuthority;

// The admin journey calls this "Deactivate account"; the API still calls the resulting status "suspended".
public record SuspendLocalAuthorityCommand(Guid Id) : IRequest<Either<OrganisationException, Organisation>>;

public class SuspendLocalAuthorityCommandHandler(
    IOrganisationRepository organisationRepository,
    IOrganisationUserRepository organisationUserRepository,
    UserDeactivator userDeactivator,
    IMfaSessionService mfaSessionService,
    IUserClaimsCache userClaimsCache,
    IAuditLogRepository auditLogRepository,
    ITransactionRunner transactionRunner,
    PermissionsService permissionsService,
    IDateTimeProvider dateTimeProvider)
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

    private static bool CanBeSuspended(Organisation organisation)
    {
        var status = organisation.OnboardingStatus.ToString();
        return status == OnboardingStatuses.Active || status == OnboardingStatuses.Invited;
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

        if (!CanBeSuspended(organisation))
        {
            return new OrganisationCannotBeSuspendedException(organisation.Id, organisation.OnboardingStatus.ToString());
        }

        // Every active user is deactivated, through the same UserDeactivator an individual deactivation uses
        // (BLUM-7667): status, audit row, MFA sessions. Then the organisation is suspended and audited.
        //
        // All of it is one transaction (BLUM-8748): each DeactivateAsync joins it, so either every user is
        // deactivated and the organisation suspended, or nothing changed. A failure leaves the organisation active
        // with every user as they were, and a retry passes the guard above and starts again from the users still
        // active. The cached claims are dropped only after the commit, so no request in between can re-cache a
        // user or the organisation as active.
        //
        // Reactivating the organisation later deliberately does not bring these users back (AC4); each one
        // is reactivated individually.
        var result = await transactionRunner.RunAsync<OrganisationException, SuspendOutcome>(
            async token =>
            {
                try
                {
                    var activeUsers = await organisationUserRepository.GetActiveUsersAsync(organisation.Id, token);
                    var cause = UserDeactivationCause.OrganisationDeactivated(organisation.Id);
                    var deactivated = new List<User>();

                    // Only LA roles. Nothing links a DHSC admin to an organisation today, and this cascade skips the
                    // guards an individual deactivation runs, so it must never be able to reach one.
                    foreach (var user in activeUsers.Where(u => u.Role.IsOrganisationMember()))
                    {
                        deactivated.Add(await userDeactivator.DeactivateAsync(user, actorId, organisation.Id, cause));
                    }

                    var suspended = await SuspendOrganisation(organisationId, actorId, token);
                    return suspended.Map(saved => new SuspendOutcome(saved, deactivated));
                }
                catch (Exception exception)
                {
                    return new OrganisationUnknownException(organisation.Id, exception);
                }
            },
            cancellationToken);

        return await result.MatchAsync<Either<OrganisationException, Organisation>>(
            async outcome =>
            {
                try
                {
                    userDeactivator.InvalidateClaims(outcome.Deactivated);
                    await BlockUsersStillActive(organisationId);
                    return outcome.Organisation;
                }
                catch (Exception exception)
                {
                    return new OrganisationUnknownException(organisation.Id, exception);
                }
            },
            exception => exception);
    }

    // The organisation is read again here rather than reusing the copy from the guard. That copy is untracked,
    // so UpdateAsync marks every column modified, and an edit committed while the users were being deactivated
    // would be silently reverted.
    private async Task<Either<OrganisationException, Organisation>> SuspendOrganisation(
        OrganisationId organisationId,
        UserId actorId,
        CancellationToken cancellationToken)
    {
        var lookup = await organisationRepository.GetOrganisationById(organisationId, cancellationToken);

        if (lookup.IsNone)
        {
            return new OrganisationDoesNotExistException(organisationId);
        }

        var organisation = lookup.ValueUnsafe();

        if (organisation.OnboardingStatus.ToString() == OnboardingStatuses.Suspended)
        {
            // A concurrent request suspended it while this one was deactivating users. That request wrote the
            // organisation's audit row; writing another would record one suspension twice.
            return organisation;
        }

        if (!CanBeSuspended(organisation))
        {
            return new OrganisationCannotBeSuspendedException(organisation.Id, organisation.OnboardingStatus.ToString());
        }

        var before = OrganisationSnapshot.Serialize(organisation);
        organisation.MarkSuspended(dateTimeProvider.GetUtcNow());
        var saved = await organisationRepository.UpdateAsync(organisation, cancellationToken);

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

    // Anyone still active now that the suspension has committed. That can be a user reactivated or added while
    // the cascade was running, or a non-LA role the loop skips. The organisation gate is what blocks them, and it
    // reads a cached claim, so clear that claim and end their sessions — what suspension did for every user before
    // the cascade existed. Without it, a user who signed back in mid-cascade keeps a cached "active" organisation
    // for up to the TTL. It runs after the commit so it also catches anyone reactivated before the commit landed.
    //
    // They are deliberately not deactivated here. Someone reactivated or added them explicitly.
    private async Task BlockUsersStillActive(OrganisationId organisationId)
    {
        var stillActive = await organisationUserRepository.GetActiveUsersAsync(organisationId, CancellationToken.None);
        foreach (var user in stillActive)
        {
            await mfaSessionService.RevokeAllUserSessionsAsync(user.Id);
            userClaimsCache.Invalidate(user.SubId);
        }
    }

    private sealed record SuspendOutcome(Organisation Organisation, IReadOnlyList<User> Deactivated);
}