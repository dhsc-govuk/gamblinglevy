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

public record ReactivateUserCommand(Guid UserId) : IRequest<Either<UserException, User>>;

public class ReactivateUserCommandHandler(
    IUserRepository repository,
    IUsersService usersService,
    IAuditLogRepository auditLogRepository,
    IUserClaimsCache userClaimsCache,
    PermissionsService permissionsService)
    : IRequestHandler<ReactivateUserCommand, Either<UserException, User>>
{
    public async Task<Either<UserException, User>> Handle(
        ReactivateUserCommand request,
        CancellationToken cancellationToken)
    {
        var actorResult = permissionsService.GetUserId();
        return await actorResult.MatchAsync<Either<UserException, User>>(
            actorId => HandleWithActor(request, actorId, cancellationToken),
            e => new UserUnknownException(new UserId(request.UserId), e));
    }

    private async Task<Either<UserException, User>> HandleWithActor(
        ReactivateUserCommand request,
        UserId actorId,
        CancellationToken cancellationToken)
    {
        var targetId = new UserId(request.UserId);
        var found = await repository.FindById(targetId, cancellationToken);
        return await found.Match<Task<Either<UserException, User>>>(
            user => ValidateAndReactivate(user, actorId, cancellationToken),
            () => Task.FromResult<Either<UserException, User>>(new UserDoesNotExistException(targetId)));
    }

    private async Task<Either<UserException, User>> ValidateAndReactivate(
        User user,
        UserId actorId,
        CancellationToken cancellationToken)
    {
        if (user.Status.Equals(UserStatus.Active))
        {
            return new UserAlreadyActiveException(user.Id);
        }

        var before = UserSnapshot.Serialize(user);
        user.Reactivate();

        var graphResult = await usersService.Enable(user.SubId.Value.ToString(), cancellationToken);
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
            // MFA session intentionally NOT invalidated here — user must re-verify MFA on next sign-in (BLUM-6015).
            userClaimsCache.Invalidate(saved.SubId);

            await auditLogRepository.AppendAsync(
                AuditLog.Create(actorId, AuditTargetType.User, saved.Id.Value, AuditAction.UserReactivated, before, UserSnapshot.Serialize(saved)),
                cancellationToken);

            return saved;
        }
        catch (Exception exception)
        {
            return new UserUnknownException(user.Id, exception);
        }
    }
}

public class ReactivateUserCommandValidator : AbstractValidator<ReactivateUserCommand>
{
    public ReactivateUserCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
    }
}