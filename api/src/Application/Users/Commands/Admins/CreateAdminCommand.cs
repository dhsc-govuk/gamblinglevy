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
using Microsoft.Extensions.Logging;

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
    IInvitationEmailService invitationEmailService,
    PermissionsService permissionsService,
    ILogger<CreateAdminCommandHandler> logger)
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
        var email = request.Email!.Trim();
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
        var identityResult = await CreateUserIdentityIfDoesNotExist(name, email, isActive, cancellationToken);
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
        try
        {
            var user = User.New(
                UserId.New(),
                name,
                email,
                new SubId(Guid.Parse(subId)),
                isActive ? UserStatus.Active : UserStatus.Deactivated,
                UserRole.Admin);

            var saved = await repository.AddAsync(user, cancellationToken);

            var invitationResult = await invitationEmailService.SendInvitationAsync(email, name, cancellationToken);
            invitationResult.IfLeft(e => logger.LogWarning(e, "Invitation email failed for {Email}; user creation not rolled back", email));

            await auditLogRepository.AppendAsync(
                AuditLog.Create(actorId, AuditTargetType.User, saved.Id.Value, AuditAction.UserCreated, before: null, after: UserSnapshot.Serialize(saved)),
                cancellationToken);

            return saved;
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