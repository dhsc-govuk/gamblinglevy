using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.Users.Audit;
using Application.Users.Exceptions;
using Application.Users.Interfaces;
using Domain.AuditLogs;
using Domain.OrganisationUsers;
using Domain.Users;
using Domain.ValueObjects;
using LanguageExt;
using MediatR;

namespace Application.Users.Commands.OrganisationUsers;

public record UpdateOrganisationUserCommand : IRequest<Either<UserException, User>>
{
    public Guid Id { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Email { get; init; }
    public bool IsActive { get; init; }

    // Kept as the raw string so an unsupported role is rejected by the validator as a 400 rather than
    // throwing out of the controller as an unhandled 500 (BLUM-7038 removed four previously-valid roles).
    public string? Role { get; init; }
}

public class UpdateOrganisationUserCommandHandler(
    PermissionsService permissionsService,
    IOrganisationUserRepository organisationUserRepository,
    IUserRepository usersRepository,
    IUsersService usersService,
    IAuditLogRepository auditLogRepository,
    IUserClaimsCache userClaimsCache,
    UserInvitations userInvitations,
    LastActiveAdminGuard lastActiveAdminGuard,
    UserDeactivator userDeactivator,
    IDateTimeProvider dateTimeProvider,
    ITransactionRunner transactionRunner)
    : IRequestHandler<UpdateOrganisationUserCommand, Either<UserException, User>>
{
    public async Task<Either<UserException, User>> Handle(
        UpdateOrganisationUserCommand request,
        CancellationToken cancellationToken)
    {
        var permissions = permissionsService.GetOrganisationPermissions();
        var actorIdResult = permissionsService.GetUserId();

        var email = request.Email!.Trim().ToLowerInvariant();
        var name = new Name(request.FirstName!, request.LastName!);

        var organisationUserId = new OrganisationUserId(request.Id);
        var userId = new UserId(request.Id);

        UserRole targetRole;
        try
        {
            targetRole = UserRole.From(request.Role!);
        }
        catch
        {
            return new UserArgumentException(userId, $"Invalid role: {request.Role}");
        }

        return await permissions.MatchAsync(
            p => actorIdResult.MatchAsync<Either<UserException, User>>(
                actorId =>
                {
                    if (!AssignableRoles.IsAssignableBy(p.UserRole, targetRole))
                    {
                        return Task.FromResult<Either<UserException, User>>(
                            new UserRoleNotAssignableException(userId, targetRole.ToString(), p.UserRole.ToString()));
                    }

                    return organisationUserRepository.GetOrganisationUserById(organisationUserId, p, cancellationToken)
                        .MatchAsync(
                            orgUser => usersRepository.FindDuplicateAsync(email, orgUser.UserId, cancellationToken)
                                .MatchAsync(
                                    existingUser => new DuplicateEmailException(existingUser.Id, existingUser.Email),
                                    () => UpdateUserDetails(actorId, orgUser, name, email, request.IsActive, targetRole, cancellationToken)),
                            () => new UserDoesNotExistException(userId));
                },
                e => new UserUnknownException(userId, e)),
            e => new UserArgumentException(UserId.Empty(), e.Message));
    }

    private async Task<Either<UserException, User>> UpdateUserDetails(
        UserId actorId,
        OrganisationUser orgUser,
        Name name,
        string email,
        bool isActive,
        UserRole role,
        CancellationToken cancellationToken)
    {
        var user = orgUser.User!;

        var deactivating = !isActive && user.Status.Equals(UserStatus.Active);
        var demoting = user.Role.Equals(UserRole.OrganisationAdmin) && !role.Equals(UserRole.OrganisationAdmin);

        // The same refusal DeactivateUserCommand gives: deactivating yourself would revoke the caller's own
        // sessions in the middle of their request.
        if (deactivating && user.Id == actorId)
        {
            return new CannotDeactivateSelfException(user.Id);
        }

        if (deactivating || demoting)
        {
            var refusal = await lastActiveAdminGuard.RefuseRemovalAsync(user, orgUser.OrganisationId, cancellationToken);
            if (refusal is not null)
            {
                return refusal;
            }
        }

        var before = UserSnapshot.Serialize(user);

        user.UpdateDetails(name, email, dateTimeProvider.GetUtcNow(), role);
        if (isActive && user.Status.Equals(UserStatus.Deactivated))
        {
            user.Reactivate(dateTimeProvider.GetUtcNow());
        }

        // Asked on every save that leaves the user active, not only one that reactivates them: a held invitation
        // whose send failed is kept, and saving the user again is the way to retry it. Never on a save that
        // deactivates them — the user is still active at this point, and the invitation would go to an account
        // that is about to be refused.
        var invitationOwed = !deactivating && await userInvitations.IsOwedAsync(user, cancellationToken);

        var graphResult = await usersService.Update(
            user.SubId.Value.ToString(),
            user.Name,
            user.Email,
            user.PasswordLoginEnabled,
            cancellationToken);

        return await graphResult.MatchAsync<Either<UserException, User>>(
            _ => PersistAndAudit(actorId, user, orgUser.OrganisationId, before, invitationOwed, deactivating, cancellationToken),
            e => new UserUnknownException(user.Id, e));
    }

    private async Task<Either<UserException, User>> PersistAndAudit(
        UserId actorId,
        User user,
        Domain.Organisations.OrganisationId organisationId,
        string before,
        bool invitationOwed,
        bool deactivating,
        CancellationToken cancellationToken)
    {
        // The change and its audit row commit together (BLUM-8748).
        var result = await transactionRunner.RunAsync<UserException, User>(
            async token =>
            {
                try
                {
                    var saved = await usersRepository.UpdateAsync(user, token);

                    await auditLogRepository.AppendAsync(
                        AuditLog.Create(
                            actorId,
                            AuditTargetType.User,
                            saved.Id.Value,
                            AuditAction.UserUpdated,
                            before,
                            UserSnapshot.Serialize(saved),
                            organisationId: organisationId),
                        token);

                    // Deactivation goes through the same sequence as every other way a user is deactivated, after
                    // the details are saved: sessions revoked and a user.deactivated row. Flipping the status here
                    // instead left a deactivated user with live sessions, so reactivating them within a session's
                    // lifetime skipped the MFA re-verification reactivation relies on (BLUM-6015, BLUM-8751).
                    //
                    // It joins this transaction (BLUM-8748), so the edit and the deactivation commit or roll back
                    // together: a failure leaves the user as they were, and resending the same edit finishes it.
                    // Joined, it leaves the claims cache to its caller, which drops it below once the status has
                    // committed — the order UserDeactivator keeps on its own.
                    if (deactivating)
                    {
                        saved = await userDeactivator.DeactivateAsync(saved, actorId, organisationId);
                    }

                    return saved;
                }
                catch (Exception exception)
                {
                    return new UserUnknownException(user.Id, exception);
                }
            },
            cancellationToken);

        // Only once committed: the claims cache, and the held invitation, which is best-effort and never fails the
        // change (and is never owed on a save that deactivates).
        return await result.MatchAsync<Either<UserException, User>>(
            async saved =>
            {
                userClaimsCache.Invalidate(saved.SubId);
                if (invitationOwed)
                {
                    await userInvitations.SendHeldAsync(saved);
                }

                return saved;
            },
            e => e);
    }
}