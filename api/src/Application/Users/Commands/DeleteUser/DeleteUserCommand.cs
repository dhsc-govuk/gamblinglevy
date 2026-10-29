using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Application.Users.Audit;
using Application.Users.Exceptions;
using Application.Users.Interfaces;
using Domain.AuditLogs;
using FluentValidation;
using LanguageExt;
using MediatR;
using Unit = LanguageExt.Unit;

namespace Application.Users.Commands.DeleteUser;

// Sent by both DELETE admin/users/{id} and DELETE organisations/{organisationId}/users/{id}.
public record DeleteUserCommand(Guid UserId) : IRequest<Either<UserException, Unit>>;

public class DeleteUserCommandHandler(
    UserTargetResolver userTargetResolver,
    LastActiveAdminGuard lastActiveAdminGuard,
    IUserRepository repository,
    IOrganisationUserRepository organisationUserRepository,
    ILocationUserRepository locationUserRepository,
    IUsersService usersService,
    IAuditLogRepository auditLogRepository,
    IMfaSessionService mfaSessionService,
    IUserClaimsCache userClaimsCache,
    ITransactionRunner transactionRunner)
    : IRequestHandler<DeleteUserCommand, Either<UserException, Unit>>
{
    public async Task<Either<UserException, Unit>> Handle(
        DeleteUserCommand request,
        CancellationToken cancellationToken)
    {
        var target = await userTargetResolver.ResolveAsync(request.UserId, cancellationToken);
        return await target.MatchAsync(
            t => ValidateAndDelete(t, cancellationToken),
            e => e);
    }

    private async Task<Either<UserException, Unit>> ValidateAndDelete(
        UserTarget target,
        CancellationToken cancellationToken)
    {
        var user = target.User;

        if (user.Id == target.ActorId)
        {
            return new CannotDeleteSelfException(user.Id);
        }

        var refusal = await lastActiveAdminGuard.RefuseRemovalAsync(user, target.OrganisationId, cancellationToken);
        if (refusal is not null)
        {
            return refusal;
        }

        var before = UserSnapshot.Serialize(user);

        var graphResult = await usersService.HardDelete(user.SubId.Value.ToString(), cancellationToken);
        return await graphResult.MatchAsync<Either<UserException, Unit>>(
            _ => RemoveAndAudit(target, before, cancellationToken),
            e => new UserUnknownException(user.Id, e));
    }

    private async Task<Either<UserException, Unit>> RemoveAndAudit(
        UserTarget target,
        string before,
        CancellationToken cancellationToken)
    {
        var user = target.User;

        // The removal, the session revocation and the audit row commit together (BLUM-8748); the claims cache is
        // dropped once they have.
        var result = await transactionRunner.RunAsync<UserException, Unit>(
            async token =>
            {
                try
                {
                    // Every row that points at the user goes first: both foreign keys are Restrict, and the Entra
                    // account is already gone by now, so a refused delete here would leave a user who can no
                    // longer sign in.
                    await organisationUserRepository.RemoveByUserIdAsync(user.Id, token);
                    await locationUserRepository.RemoveByUserIdAsync(user.Id, token);
                    await repository.RemoveAsync(user, token);
                    await mfaSessionService.RevokeAllUserSessionsAsync(user.Id);

                    await auditLogRepository.AppendAsync(
                        AuditLog.Create(
                            target.ActorId,
                            AuditTargetType.User,
                            user.Id.Value,
                            AuditAction.UserDeleted,
                            before,
                            after: null,
                            organisationId: target.OrganisationId),
                        token);

                    return Unit.Default;
                }
                catch (Exception exception)
                {
                    return new UserUnknownException(user.Id, exception);
                }
            },
            cancellationToken);

        result.IfRight(_ => userClaimsCache.Invalidate(user.SubId));
        return result;
    }
}

public class DeleteUserCommandValidator : AbstractValidator<DeleteUserCommand>
{
    public DeleteUserCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
    }
}