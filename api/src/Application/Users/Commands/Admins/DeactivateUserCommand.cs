using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.Users.Audit;
using Application.Users.Exceptions;
using Application.Users.Interfaces;
using Domain.AuditLogs;
using Domain.Users;
using Domain.ValueObjects;
using FluentValidation;
using LanguageExt;
using MediatR;

namespace Application.Users.Commands.Admins;

public record DeactivateUserCommand(Guid UserId) : IRequest<Either<UserException, User>>;

public class DeactivateUserCommandHandler(
    IUserRepository repository,
    IUsersService usersService,
    IAuditLogRepository auditLogRepository,
    IMfaSessionService mfaSessionService,
    IUserClaimsCache userClaimsCache,
    PermissionsService permissionsService)
    : IRequestHandler<DeactivateUserCommand, Either<UserException, User>>
{
    public async Task<Either<UserException, User>> Handle(
        DeactivateUserCommand request,
        CancellationToken cancellationToken)
    {
        var actorResult = permissionsService.GetUserId();
        return await actorResult.MatchAsync<Either<UserException, User>>(
            actorId => HandleWithActor(request, actorId, cancellationToken),
            e => new UserUnknownException(new UserId(request.UserId), e));
    }

    private async Task<Either<UserException, User>> HandleWithActor(
        DeactivateUserCommand request,
        UserId actorId,
        CancellationToken cancellationToken)
    {
        var targetId = new UserId(request.UserId);
        var found = await repository.FindById(targetId, cancellationToken);
        return await found.Match<Task<Either<UserException, User>>>(
            user => ValidateAndDeactivate(user, actorId, cancellationToken),
            () => Task.FromResult<Either<UserException, User>>(new UserDoesNotExistException(targetId)));
    }

    private async Task<Either<UserException, User>> ValidateAndDeactivate(
        User user,
        UserId actorId,
        CancellationToken cancellationToken)
    {
        if (user.Status.Equals(UserStatus.Deactivated))
        {
            return new UserAlreadyDeactivatedException(user.Id);
        }

        if (user.Id == actorId)
        {
            return new CannotDeactivateSelfException(user.Id);
        }

        if (user.Role.Equals(UserRole.Admin))
        {
            var admins = await repository.GetActiveAdminsAsync(cancellationToken);
            if (admins.Count == 1 && admins[0].Id == user.Id)
            {
                return new CannotDeactivateOnlyAdminException(user.Id);
            }
        }

        var before = UserSnapshot.Serialize(user);
        user.Deactivate();

        var graphResult = await usersService.Disable(user.SubId.Value.ToString(), cancellationToken);
        return await graphResult.MatchAsync<Either<UserException, User>>(
            _ => PersistAndAudit(user, actorId, before, cancellationToken),
            e => new UserUnknownException(user.Id, e));
    }

    private async Task<Either<UserException, User>> PersistAndAudit(
        User user,
        UserId actorId,
        string before,
        CancellationToken cancellationToken)
    {
        try
        {
            var saved = await repository.UpdateAsync(user, cancellationToken);
            await mfaSessionService.RevokeAllUserSessionsAsync(saved.Id);
            userClaimsCache.Invalidate(saved.SubId);

            await auditLogRepository.AppendAsync(
                AuditLog.Create(actorId, AuditTargetType.User, saved.Id.Value, AuditAction.UserDeactivated, before, UserSnapshot.Serialize(saved)),
                cancellationToken);

            return saved;
        }
        catch (Exception exception)
        {
            return new UserUnknownException(user.Id, exception);
        }
    }
}

public class DeactivateUserCommandValidator : AbstractValidator<DeactivateUserCommand>
{
    public DeactivateUserCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
    }
}