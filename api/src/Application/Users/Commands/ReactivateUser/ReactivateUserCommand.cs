using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Application.Users.Audit;
using Application.Users.Exceptions;
using Application.Users.Interfaces;
using Domain.AuditLogs;
using Domain.Organisations;
using Domain.Users;
using Domain.ValueObjects;
using FluentValidation;
using LanguageExt;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Users.Commands.ReactivateUser;

public record ReactivateUserCommand(Guid UserId) : IRequest<Either<UserException, User>>;

public class ReactivateUserCommandHandler(
    UserTargetResolver userTargetResolver,
    IUserRepository repository,
    IUsersService usersService,
    IAuditLogRepository auditLogRepository,
    IUserClaimsCache userClaimsCache,
    UserInvitations userInvitations,
    IDateTimeProvider dateTimeProvider,
    ITransactionRunner transactionRunner,
    ILogger<ReactivateUserCommandHandler> logger)
    : IRequestHandler<ReactivateUserCommand, Either<UserException, User>>
{
    public async Task<Either<UserException, User>> Handle(
        ReactivateUserCommand request,
        CancellationToken cancellationToken)
    {
        var target = await userTargetResolver.ResolveAsync(request.UserId, cancellationToken);
        return await target.MatchAsync(
            t => ValidateAndReactivate(t.User, t.ActorId, t.OrganisationId, cancellationToken),
            e => e);
    }

    private async Task<Either<UserException, User>> ValidateAndReactivate(
        User user,
        UserId actorId,
        OrganisationId? organisationId,
        CancellationToken cancellationToken)
    {
        if (user.Status.Equals(UserStatus.Active))
        {
            return new UserAlreadyActiveException(user.Id);
        }

        var before = UserSnapshot.Serialize(user);
        user.Reactivate(dateTimeProvider.GetUtcNow());
        var invitationOwed = await userInvitations.IsOwedAsync(user, cancellationToken);

        // Best-effort, and deliberately not allowed to fail the reactivation (BLUM-6386). Since deactivation
        // stopped disabling the CIAM account, this call only repairs accounts disabled by the old behaviour —
        // for anyone created since, it is a no-op. Folding a Graph outage into UserUnknownException left the
        // DB row deactivated, so the middleware kept refusing a user whose CIAM account never needed touching:
        // the same "refused for a reason we cannot explain" failure this ticket removed from the deactivate
        // path. Local UserStatus is the source of truth, so persist it either way and log the repair failure.
        var graphResult = await usersService.Enable(user.SubId.Value.ToString(), cancellationToken);
        graphResult.IfLeft(e => logger.LogWarning(
            e,
            "Graph Enable failed for {SubId} during reactivation; local status reactivated anyway. The CIAM "
            + "account may still be disabled from before BLUM-6386 and need re-enabling by hand",
            user.SubId.Value));

        return await PersistAndAudit(user, actorId, organisationId, before, invitationOwed, cancellationToken);
    }

    private async Task<Either<UserException, User>> PersistAndAudit(
        User user,
        UserId actorId,
        OrganisationId? organisationId,
        string before,
        bool invitationOwed,
        CancellationToken cancellationToken)
    {
        // The reactivation and its audit row commit together (BLUM-8748).
        var result = await transactionRunner.RunAsync<UserException, User>(
            async token =>
            {
                try
                {
                    var saved = await repository.UpdateAsync(user, token);
                    // MFA session intentionally NOT invalidated here — user must re-verify MFA on next sign-in (BLUM-6015).

                    await auditLogRepository.AppendAsync(
                        AuditLog.Create(actorId, AuditTargetType.User, saved.Id.Value, AuditAction.UserReactivated, before, UserSnapshot.Serialize(saved), organisationId),
                        token);

                    return saved;
                }
                catch (Exception exception)
                {
                    return new UserUnknownException(user.Id, exception);
                }
            },
            cancellationToken);

        // Only once committed: the held invitation is best-effort and never fails the reactivation.
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

public class ReactivateUserCommandValidator : AbstractValidator<ReactivateUserCommand>
{
    public ReactivateUserCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
    }
}