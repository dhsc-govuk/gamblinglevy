using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Application.Users.Exceptions;
using Application.Users.Interfaces;
using Domain.AuditLogs;
using Domain.Organisations;
using Domain.Users;
using FluentValidation;
using LanguageExt;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Users.Commands.ResetUserPassword;

public record ResetUserPasswordCommand(Guid UserId) : IRequest<Either<UserException, User>>;

public class ResetUserPasswordCommandHandler(
    UserTargetResolver userTargetResolver,
    IUsersService usersService,
    ITemporaryPasswordGenerator passwordGenerator,
    ITemporaryPasswordEmailService temporaryPasswordEmailService,
    IMfaSessionService mfaSessionService,
    IAuditLogRepository auditLogRepository,
    ITransactionRunner transactionRunner,
    ILogger<ResetUserPasswordCommandHandler> logger)
    : IRequestHandler<ResetUserPasswordCommand, Either<UserException, User>>
{
    public async Task<Either<UserException, User>> Handle(
        ResetUserPasswordCommand request,
        CancellationToken cancellationToken)
    {
        var target = await userTargetResolver.ResolveAsync(request.UserId, cancellationToken);
        return await target.MatchAsync(
            t => ResetPassword(t.User, t.ActorId, t.OrganisationId, cancellationToken),
            e => e);
    }

    private async Task<Either<UserException, User>> ResetPassword(
        User user,
        UserId actorId,
        OrganisationId? organisationId,
        CancellationToken cancellationToken)
    {
        if (!user.PasswordLoginEnabled)
        {
            return new PasswordLoginNotEnabledException(user.Id);
        }

        // Checked before ResetPassword below replaces the user's working password with an unknown temporary
        // one and revokes every session — see EnableUserPasswordLoginCommand for why this refusal happens here
        // rather than after.
        if (!temporaryPasswordEmailService.IsConfigured)
        {
            return new TemporaryPasswordEmailNotConfiguredException(user.Id);
        }

        try
        {
            var temporaryPassword = passwordGenerator.Generate();

            var graphResult = await usersService.ResetPassword(
                user.SubId.Value.ToString(),
                user.Email,
                temporaryPassword,
                cancellationToken);

            return await graphResult.MatchAsync<Either<UserException, User>>(
                async _ =>
                {
                    // The session revocation and its audit row commit together (BLUM-8748); the email goes out
                    // only once they have.
                    await transactionRunner.RunAsync(
                        async token =>
                        {
                            await mfaSessionService.RevokeAllUserSessionsAsync(user.Id);

                            await auditLogRepository.AppendAsync(
                                AuditLog.Create(actorId, AuditTargetType.User, user.Id.Value, AuditAction.UserPasswordReset, null, null, organisationId),
                                token);
                        },
                        cancellationToken);

                    var emailResult = await temporaryPasswordEmailService.SendTemporaryPasswordAsync(
                        user.Email,
                        new TemporaryPasswordNotification(user.Name.ToString(), temporaryPassword),
                        cancellationToken);

                    // The reset already happened in Entra and every session is already revoked — the audit log
                    // reflects that regardless — but a failed send must not be reported as success, since it is
                    // the only way this user learns their new password.
                    return emailResult.Match<Either<UserException, User>>(
                        _ => user,
                        e =>
                        {
                            logger.LogWarning(e, "Temporary password email failed for {UserId}", user.Id);
                            return new TemporaryPasswordEmailFailedException(user.Id, e);
                        });
                },
                e => new UserUnknownException(user.Id, e));
        }
        catch (Exception exception)
        {
            return new UserUnknownException(user.Id, exception);
        }
    }
}

public class ResetUserPasswordCommandValidator : AbstractValidator<ResetUserPasswordCommand>
{
    public ResetUserPasswordCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
    }
}