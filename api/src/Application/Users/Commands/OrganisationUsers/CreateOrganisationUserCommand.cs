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
    UserInvitations userInvitations,
    IDateTimeProvider dateTimeProvider,
    ITransactionRunner transactionRunner)
    : IRequestHandler<CreateOrganisationUserCommand, Either<UserException, User>>
{
    public async Task<Either<UserException, User>> Handle(
        CreateOrganisationUserCommand request,
        CancellationToken cancellationToken)
    {
        var permission = permissionsService.GetOrganisationPermissions();
        var actorIdResult = permissionsService.GetUserId();

        var organisationId = new OrganisationId(request.OrganisationId);
        var email = request.Email!.Trim().ToLowerInvariant();
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
                                    org => CreateUserIdentity(actorId, org, name, email, request.IsActive, targetRole, cancellationToken),
                                    () => new UserOrganisationDoesNotExistException(UserId.Empty(), organisationId)));
                },
                e => new UserUnknownException(UserId.Empty(), e)),
            e => new UserArgumentException(UserId.Empty(), e.Message));
    }

    private async Task<Either<UserException, User>> CreateUserIdentity(
        UserId actorId,
        Organisation organisation,
        Name name,
        string email,
        bool isActive,
        UserRole role,
        CancellationToken cancellationToken)
    {
        var identityResult = await CreateUserIdentityIfDoesNotExist(name, email, cancellationToken);
        return await identityResult.MatchAsync<Either<UserException, User>>(
            subId => PersistUser(actorId, organisation, name, email, subId, isActive, role, cancellationToken),
            e => new UserUnknownException(UserId.Empty(), e));
    }

    private async Task<Either<UserException, User>> PersistUser(
        UserId actorId,
        Organisation organisation,
        Name name,
        string email,
        string subId,
        bool isActive,
        UserRole role,
        CancellationToken cancellationToken)
    {
        // The user, their membership and the audit row commit together (BLUM-8748); the invitation goes out only
        // once they have.
        var result = await transactionRunner.RunAsync<UserException, User>(
            async token =>
            {
                try
                {
                    var user = User.New(
                        UserId.New(),
                        name,
                        email,
                        new SubId(Guid.Parse(subId)),
                        isActive ? UserStatus.Active : UserStatus.Deactivated,
                        role,
                        dateTimeProvider.GetUtcNow());
                    // Decided here, not by the caller: a user added to a deactivated organisation cannot sign in yet
                    // whatever status they were given, so their invitation waits for the organisation (BLUM-7945).
                    userInvitations.HoldIfCannotSignIn(user, organisation);
                    var orgId = organisation.Id;
                    var organisationUserId = new OrganisationUserId(user.Id.Value);
                    var organisationUser = OrganisationUser.New(organisationUserId, user.Id, orgId, user);
                    await organisationUsersRepository.AddAsync(organisationUser, token);

                    await auditLogRepository.AppendAsync(
                        AuditLog.Create(
                            actorId,
                            AuditTargetType.User,
                            user.Id.Value,
                            AuditAction.UserCreated,
                            before: null,
                            after: UserSnapshot.Serialize(user),
                            organisationId: orgId),
                        token);

                    return user;
                }
                catch (Exception exception)
                {
                    return new UserUnknownException(UserId.Empty(), exception);
                }
            },
            cancellationToken);

        return await result.MatchAsync<Either<UserException, User>>(
            async user =>
            {
                await userInvitations.SendUnlessHeldAsync(user, cancellationToken);
                return user;
            },
            e => e);
    }

    private async Task<Either<Exception, string>> CreateUserIdentityIfDoesNotExist(
        Name userName,
        string email,
        CancellationToken cancellationToken)
    {
        // A refused lookup is returned, not treated as "no such account": creating one on the strength of
        // a lookup that never answered is how a second directory object for the same address gets made.
        var lookup = await usersService.FindByEmail(email, cancellationToken);
        return await lookup.MatchAsync<Either<Exception, string>>(
            existingUserSubId => existingUserSubId.MatchAsync(
                subId => subId,
                () => usersService.Add(userName, email, cancellationToken)),
            e => e);
    }
}