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
using Unit = LanguageExt.Unit;

namespace Application.Users.Commands.Admins;

public record DeleteUserCommand(Guid UserId) : IRequest<Either<UserException, Unit>>;

public class DeleteUserCommandHandler(
    IUserRepository repository,
    IOrganisationUserRepository organisationUserRepository,
    ILocationUserRepository locationUserRepository,
    IUsersService usersService,
    IAuditLogRepository auditLogRepository,
    IMfaSessionService mfaSessionService,
    IUserClaimsCache userClaimsCache,
    PermissionsService permissionsService)
    : IRequestHandler<DeleteUserCommand, Either<UserException, Unit>>
{
    public async Task<Either<UserException, Unit>> Handle(
        DeleteUserCommand request,
        CancellationToken cancellationToken)
    {
        var actorResult = permissionsService.GetUserId();
        return await actorResult.MatchAsync<Either<UserException, Unit>>(
            actorId => HandleWithActor(request, actorId, cancellationToken),
            e => new UserUnknownException(new UserId(request.UserId), e));
    }

    private async Task<Either<UserException, Unit>> HandleWithActor(
        DeleteUserCommand request,
        UserId actorId,
        CancellationToken cancellationToken)
    {
        var targetId = new UserId(request.UserId);
        var found = await repository.FindById(targetId, cancellationToken);
        return await found.Match<Task<Either<UserException, Unit>>>(
            user => ValidateAndDelete(user, actorId, cancellationToken),
            () => Task.FromResult<Either<UserException, Unit>>(new UserDoesNotExistException(targetId)));
    }

    private async Task<Either<UserException, Unit>> ValidateAndDelete(
        User user,
        UserId actorId,
        CancellationToken cancellationToken)
    {
        if (user.Id == actorId)
        {
            return new CannotDeleteSelfException(user.Id);
        }

        if (user.Role.Equals(UserRole.Admin) && user.Status.Equals(UserStatus.Active))
        {
            var admins = await repository.GetActiveAdminsAsync(cancellationToken);
            if (admins.Count == 1 && admins[0].Id == user.Id)
            {
                return new CannotDeleteOnlyAdminException(user.Id);
            }
        }

        var before = UserSnapshot.Serialize(user);

        var graphResult = await usersService.HardDelete(user.SubId.Value.ToString(), cancellationToken);
        return await graphResult.MatchAsync<Either<UserException, Unit>>(
            _ => RemoveAndAudit(user, actorId, before, cancellationToken),
            e => new UserUnknownException(user.Id, e));
    }

    private async Task<Either<UserException, Unit>> RemoveAndAudit(
        User user,
        UserId actorId,
        string before,
        CancellationToken cancellationToken)
    {
        try
        {
            await organisationUserRepository.RemoveByUserIdAsync(user.Id, cancellationToken);
            await locationUserRepository.RemoveByUserIdAsync(user.Id, cancellationToken);
            await repository.RemoveAsync(user, cancellationToken);
            await mfaSessionService.RevokeAllUserSessionsAsync(user.Id);
            userClaimsCache.Invalidate(user.SubId);

            await auditLogRepository.AppendAsync(
                AuditLog.Create(actorId, AuditTargetType.User, user.Id.Value, AuditAction.UserDeleted, before, after: null),
                cancellationToken);

            return Unit.Default;
        }
        catch (Exception exception)
        {
            return new UserUnknownException(user.Id, exception);
        }
    }
}

public class DeleteUserCommandValidator : AbstractValidator<DeleteUserCommand>
{
    public DeleteUserCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
    }
}