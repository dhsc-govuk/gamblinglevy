using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.Users.Audit;
using Application.Users.Exceptions;
using Application.Users.Interfaces;
using Domain.AuditLogs;
using Domain.Users;
using Domain.ValueObjects;
using LanguageExt;
using MediatR;

namespace Application.Users.Commands.Admins;

public record CreateAdminCommand : IRequest<Either<UserException, User>>
{
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Email { get; init; }
    public bool IsActive { get; init; } = true;
}

public class CreateAdminCommandHandler(
    IUserRepository repository,
    IUsersService usersService,
    IAuditLogRepository auditLogRepository,
    UserInvitations userInvitations,
    PermissionsService permissionsService,
    IDateTimeProvider dateTimeProvider,
    ITransactionRunner transactionRunner)
    : IRequestHandler<CreateAdminCommand, Either<UserException, User>>
{
    public async Task<Either<UserException, User>> Handle(
        CreateAdminCommand request,
        CancellationToken cancellationToken)
    {
        var actorResult = permissionsService.GetUserId();
        return await actorResult.MatchAsync<Either<UserException, User>>(
            actorId => HandleWithActor(request, actorId, cancellationToken),
            e => new UserUnknownException(UserId.Empty(), e));
    }

    private async Task<Either<UserException, User>> HandleWithActor(
        CreateAdminCommand request,
        UserId actorId,
        CancellationToken cancellationToken)
    {
        var email = request.Email!.Trim().ToLowerInvariant();
        var name = new Name(request.FirstName!, request.LastName!);

        var userWithEmail = await repository.FindByEmailAsync(email, cancellationToken);
        return await userWithEmail.MatchAsync(
            user => new DuplicateEmailException(user.Id, user.Email),
            () => CreateUser(actorId, name, email, request.IsActive, cancellationToken));
    }

    private async Task<Either<UserException, User>> CreateUser(
        UserId actorId,
        Name name,
        string email,
        bool isActive,
        CancellationToken cancellationToken)
    {
        var identityResult = await CreateUserIdentityIfDoesNotExist(name, email, cancellationToken);
        return await identityResult.MatchAsync<Either<UserException, User>>(
            subId => PersistUser(actorId, name, email, subId, isActive, cancellationToken),
            e => new UserUnknownException(UserId.Empty(), e));
    }

    private async Task<Either<UserException, User>> PersistUser(
        UserId actorId,
        Name name,
        string email,
        string subId,
        bool isActive,
        CancellationToken cancellationToken)
    {
        // The user and its audit row commit together (BLUM-8748); the invitation goes out only once they have.
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
                        UserRole.Admin,
                        dateTimeProvider.GetUtcNow());
                    userInvitations.HoldIfCannotSignIn(user, organisation: null);

                    var saved = await repository.AddAsync(user, token);

                    await auditLogRepository.AppendAsync(
                        AuditLog.Create(actorId, AuditTargetType.User, saved.Id.Value, AuditAction.UserCreated, before: null, after: UserSnapshot.Serialize(saved)),
                        token);

                    return saved;
                }
                catch (Exception exception)
                {
                    return new UserUnknownException(UserId.Empty(), exception);
                }
            },
            cancellationToken);

        return await result.MatchAsync<Either<UserException, User>>(
            async saved =>
            {
                await userInvitations.SendUnlessHeldAsync(saved, cancellationToken);
                return saved;
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