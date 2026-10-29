using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Application.Users.Audit;
using Application.Users.Exceptions;
using Domain.AuditLogs;
using Domain.Organisations;
using Domain.Users;
using FluentValidation;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using MediatR;

namespace Application.Users.Commands.ResetUserMfa;

public record ResetUserMfaCommand(Guid UserId) : IRequest<Either<UserException, User>>;

public class ResetUserMfaCommandHandler(
    UserTargetResolver userTargetResolver,
    IUserMfaRepository userMfaRepository,
    IAuditLogRepository auditLogRepository,
    IMfaSessionService mfaSessionService,
    IUserClaimsCache userClaimsCache,
    ITransactionRunner transactionRunner)
    : IRequestHandler<ResetUserMfaCommand, Either<UserException, User>>
{
    public async Task<Either<UserException, User>> Handle(
        ResetUserMfaCommand request,
        CancellationToken cancellationToken)
    {
        var target = await userTargetResolver.ResolveAsync(request.UserId, cancellationToken);
        return await target.MatchAsync(
            t => ResetMfa(t.User, t.ActorId, t.OrganisationId, cancellationToken),
            e => e);
    }

    private async Task<Either<UserException, User>> ResetMfa(
        User user,
        UserId actorId,
        OrganisationId? organisationId,
        CancellationToken cancellationToken)
    {
        // The enrolment removal, the session revocation and the audit row commit together (BLUM-8748); the claims
        // cache is dropped once they have.
        var result = await transactionRunner.RunAsync<UserException, User>(
            async token =>
            {
                try
                {
                    var before = UserSnapshot.Serialize(user);

                    var mfa = await userMfaRepository.GetByUserIdAsync(user.Id, token);
                    if (mfa.IsSome)
                    {
                        await userMfaRepository.DeleteAsync(mfa.ValueUnsafe(), token);
                    }

                    await mfaSessionService.RevokeAllUserSessionsAsync(user.Id);

                    await auditLogRepository.AppendAsync(
                        AuditLog.Create(actorId, AuditTargetType.User, user.Id.Value, AuditAction.UserMfaReset, before, null, organisationId),
                        token);

                    return user;
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

public class ResetUserMfaCommandValidator : AbstractValidator<ResetUserMfaCommand>
{
    public ResetUserMfaCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
    }
}