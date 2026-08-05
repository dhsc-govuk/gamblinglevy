using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.Users.Audit;
using Application.Users.Exceptions;
using Domain.AuditLogs;
using Domain.Users;
using FluentValidation;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using MediatR;

namespace Application.Users.Commands.Admins;

public record ResetUserMfaCommand(Guid UserId) : IRequest<Either<UserException, User>>;

public class ResetUserMfaCommandHandler(
    IUserRepository repository,
    IUserMfaRepository userMfaRepository,
    IAuditLogRepository auditLogRepository,
    IMfaSessionService mfaSessionService,
    IUserClaimsCache userClaimsCache,
    PermissionsService permissionsService)
    : IRequestHandler<ResetUserMfaCommand, Either<UserException, User>>
{
    public async Task<Either<UserException, User>> Handle(
        ResetUserMfaCommand request,
        CancellationToken cancellationToken)
    {
        var actorResult = permissionsService.GetUserId();
        return await actorResult.MatchAsync<Either<UserException, User>>(
            actorId => HandleWithActor(request, actorId, cancellationToken),
            e => new UserUnknownException(new UserId(request.UserId), e));
    }

    private async Task<Either<UserException, User>> HandleWithActor(
        ResetUserMfaCommand request,
        UserId actorId,
        CancellationToken cancellationToken)
    {
        var targetId = new UserId(request.UserId);
        var found = await repository.FindById(targetId, cancellationToken);
        return await found.Match<Task<Either<UserException, User>>>(
            user => ResetMfa(user, actorId, cancellationToken),
            () => Task.FromResult<Either<UserException, User>>(new UserDoesNotExistException(targetId)));
    }

    private async Task<Either<UserException, User>> ResetMfa(
        User user,
        UserId actorId,
        CancellationToken cancellationToken)
    {
        try
        {
            var before = UserSnapshot.Serialize(user);

            var mfa = await userMfaRepository.GetByUserIdAsync(user.Id, cancellationToken);
            if (mfa.IsSome)
            {
                await userMfaRepository.DeleteAsync(mfa.ValueUnsafe(), cancellationToken);
            }

            await mfaSessionService.RevokeAllUserSessionsAsync(user.Id);
            userClaimsCache.Invalidate(user.SubId);

            await auditLogRepository.AppendAsync(
                AuditLog.Create(actorId, AuditTargetType.User, user.Id.Value, AuditAction.UserMfaReset, before, null),
                cancellationToken);

            return user;
        }
        catch (Exception exception)
        {
            return new UserUnknownException(user.Id, exception);
        }
    }
}

public class ResetUserMfaCommandValidator : AbstractValidator<ResetUserMfaCommand>
{
    public ResetUserMfaCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
    }
}