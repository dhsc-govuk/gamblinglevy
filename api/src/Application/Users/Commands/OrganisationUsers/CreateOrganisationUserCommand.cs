using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.Users.Audit;
using Application.Users.Exceptions;
using Application.Users.Interfaces;
using Domain.AuditLogs;
using Domain.Organisations;
using Domain.OrganisationUsers;
using Domain.Users;
using Domain.ValueObjects;
using LanguageExt;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Users.Commands.OrganisationUsers;

public record CreateOrganisationUserCommand : IRequest<Either<UserException, User>>
{
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Email { get; init; }
    public bool IsActive { get; init; }
    public Guid OrganisationId { get; init; }

    // Kept as the raw string so an unsupported role is rejected by the validator as a 400 rather than
    // throwing out of the controller as an unhandled 500 (BLUM-7038 removed four previously-valid roles).
    public string? Role { get; init; }
}

public class CreateOrganisationUserCommandHandler(
    PermissionsService permissionsService,
    IOrganisationRepository organisationRepository,
    IOrganisationUserRepository organisationUsersRepository,
    IUserRepository usersRepository,
    IUsersService usersService,
    IAuditLogRepository auditLogRepository,
    IInvitationEmailService invitationEmailService,
    ILogger<CreateOrganisationUserCommandHandler> logger)
    : IRequestHandler<CreateOrganisationUserCommand, Either<UserException, User>>
{
    public async Task<Either<UserException, User>> Handle(
        CreateOrganisationUserCommand request,
        CancellationToken cancellationToken)
    {
        var permission = permissionsService.GetOrganisationPermissions();
        var actorIdResult = permissionsService.GetUserId();

        var organisationId = new OrganisationId(request.OrganisationId);
        var email = request.Email!.Trim();
        var name = new Name(request.FirstName!, request.LastName!);

        UserRole targetRole;
        try
        {
            targetRole = UserRole.From(request.Role!);
        }
        catch
        {
            return new UserArgumentException(UserId.Empty(), $"Invalid role: {request.Role}");
        }

        return await permission.MatchAsync(
            p => actorIdResult.MatchAsync<Either<UserException, User>>(
                actorId =>
                {
                    if (!AssignableRoles.IsAssignableBy(p.UserRole, targetRole))
                    {
                        return Task.FromResult<Either<UserException, User>>(
                            new UserRoleNotAssignableException(UserId.Empty(), targetRole.ToString(), p.UserRole.ToString()));
                    }

                    return usersRepository.FindByEmailAsync(email, cancellationToken)
                        .MatchAsync(
                            existingUser => new UserExistsException(existingUser.Id, existingUser.Email),
                            () => organisationRepository.GetOrganisationById(organisationId, p, cancellationToken)
                                .MatchAsync(
                                    org => CreateUserIdentity(actorId, org.Id, name, email, request.IsActive, targetRole, cancellationToken),
                                    () => new UserOrganisationDoesNotExistException(UserId.Empty(), organisationId)));
                },
                e => new UserUnknownException(UserId.Empty(), e)),
            e => new UserArgumentException(UserId.Empty(), e.Message));
    }

    private async Task<Either<UserException, User>> CreateUserIdentity(
        UserId actorId,
        OrganisationId orgId,
        Name name,
        string email,
        bool isActive,
        UserRole role,
        CancellationToken cancellationToken)
    {
        var identityResult = await CreateUserIdentityIfDoesNotExist(name, email, isActive, cancellationToken);
        return await identityResult.MatchAsync<Either<UserException, User>>(
            subId => PersistUser(actorId, orgId, name, email, subId, isActive, role, cancellationToken),
            e => new UserUnknownException(UserId.Empty(), e));
    }

    private async Task<Either<UserException, User>> PersistUser(
        UserId actorId,
        OrganisationId orgId,
        Name name,
        string email,
        string subId,
        bool isActive,
        UserRole role,
        CancellationToken cancellationToken)
    {
        try
        {
            var user = User.New(
                UserId.New(),
                name,
                email,
                new SubId(Guid.Parse(subId)),
                isActive ? UserStatus.Active : UserStatus.Deactivated,
                role);
            var organisationUserId = new OrganisationUserId(user.Id.Value);
            var organisationUser = OrganisationUser.New(organisationUserId, user.Id, orgId, user);
            await organisationUsersRepository.AddAsync(organisationUser, cancellationToken);

            var invitationResult = await invitationEmailService.SendInvitationAsync(email, name, cancellationToken);
            invitationResult.IfLeft(e => logger.LogWarning(e, "Invitation email failed for {Email}; user creation not rolled back", email));

            await auditLogRepository.AppendAsync(
                AuditLog.Create(
                    actorId,
                    AuditTargetType.User,
                    user.Id.Value,
                    AuditAction.UserCreated,
                    before: null,
                    after: UserSnapshot.Serialize(user),
                    organisationId: orgId),
                cancellationToken);

            return user;
        }
        catch (Exception exception)
        {
            return new UserUnknownException(UserId.Empty(), exception);
        }
    }

    private async Task<Either<Exception, string>> CreateUserIdentityIfDoesNotExist(
        Name userName,
        string email,
        bool isActive,
        CancellationToken cancellationToken)
    {
        var existingUserSubId = await usersService.FindByEmail(email, cancellationToken);
        return await existingUserSubId.MatchAsync(
            subId => subId,
            () => usersService.Add(userName, email, isActive, cancellationToken));
    }
}