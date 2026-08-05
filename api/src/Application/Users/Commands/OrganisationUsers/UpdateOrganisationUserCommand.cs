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
    IUserClaimsCache userClaimsCache)
    : IRequestHandler<UpdateOrganisationUserCommand, Either<UserException, User>>
{
    public async Task<Either<UserException, User>> Handle(
        UpdateOrganisationUserCommand request,
        CancellationToken cancellationToken)
    {
        var permissions = permissionsService.GetOrganisationPermissions();
        var actorIdResult = permissionsService.GetUserId();

        var email = request.Email!.Trim();
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

        if (user.Role.Equals(UserRole.OrganisationAdmin) && !role.Equals(UserRole.OrganisationAdmin))
        {
            var activeAdmins = await organisationUserRepository.GetActiveOrgAdminsAsync(orgUser.OrganisationId, cancellationToken);
            if (activeAdmins.Count <= 1 && activeAdmins.Any(a => a.Id == user.Id))
            {
                return new CannotDeactivateOnlyOrgAdminException(user.Id, orgUser.OrganisationId);
            }
        }

        var before = UserSnapshot.Serialize(user);

        user.UpdateDetails(name, email, role);
        if (isActive && user.Status.Equals(UserStatus.Deactivated))
        {
            user.Reactivate();
        }
        else if (!isActive && user.Status.Equals(UserStatus.Active))
        {
            user.Deactivate();
        }

        var graphResult = await usersService.Update(
            user.SubId.Value.ToString(),
            user.Name,
            user.Email,
            cancellationToken);

        return await graphResult.MatchAsync<Either<UserException, User>>(
            _ => PersistAndAudit(actorId, user, orgUser.OrganisationId, before, cancellationToken),
            e => new UserUnknownException(user.Id, e));
    }

    private async Task<Either<UserException, User>> PersistAndAudit(
        UserId actorId,
        User user,
        Domain.Organisations.OrganisationId organisationId,
        string before,
        CancellationToken cancellationToken)
    {
        try
        {
            var saved = await usersRepository.UpdateAsync(user, cancellationToken);
            userClaimsCache.Invalidate(saved.SubId);

            await auditLogRepository.AppendAsync(
                AuditLog.Create(
                    actorId,
                    AuditTargetType.User,
                    saved.Id.Value,
                    AuditAction.UserUpdated,
                    before,
                    UserSnapshot.Serialize(saved),
                    organisationId: organisationId),
                cancellationToken);

            return saved;
        }
        catch (Exception exception)
        {
            return new UserUnknownException(user.Id, exception);
        }
    }
}