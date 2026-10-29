using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Application.Users.Audit;
using Application.Users.Exceptions;
using Application.Users.Interfaces;
using Domain.AuditLogs;
using Domain.Organisations;
using Domain.Users;
using FluentValidation;
using LanguageExt;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Users.Commands.EnableUserPasswordLogin;

public record EnableUserPasswordLoginCommand(Guid UserId) : IRequest<Either<UserException, User>>;

public class EnableUserPasswordLoginCommandHandler(
    UserTargetResolver userTargetResolver,
    IUserRepository repository,
    IUsersService usersService,
    ITemporaryPasswordGenerator passwordGenerator,
    ITemporaryPasswordEmailService temporaryPasswordEmailService,
    IMfaSessionService mfaSessionService,
    IAuditLogRepository auditLogRepository,
    IDateTimeProvider dateTimeProvider,
    ITransactionRunner transactionRunner,
    ILogger<EnableUserPasswordLoginCommandHandler> logger)
    : IRequestHandler<EnableUserPasswordLoginCommand, Either<UserException, User>>
{
    public async Task<Either<UserException, User>> Handle(
        EnableUserPasswordLoginCommand request,
        CancellationToken cancellationToken)
    {
        var target = await userTargetResolver.ResolveAsync(request.UserId, cancellationToken);
        return await target.MatchAsync(
            t => EnablePasswordLogin(t.User, t.ActorId, t.OrganisationId, cancellationToken),
            e => e);
    }

    private async Task<Either<UserException, User>> EnablePasswordLogin(
        User user,
        UserId actorId,
        OrganisationId? organisationId,
        CancellationToken cancellationToken)
    {
        // Checked before anything in Entra is touched: once EnablePasswordLogin below replaces the user's
        // passwordless identity with a password one and every session is revoked, the temporary password email
        // is the only way they find out how to sign back in. Refusing here, rather than after, is what stops a
        // missing template — the state every environment ships in until DHSC creates it — from locking someone
        // out with no way to tell them their new password.
        if (!temporaryPasswordEmailService.IsConfigured)
        {
            return new TemporaryPasswordEmailNotConfiguredException(user.Id);
        }

        var before = UserSnapshot.Serialize(user);
        var temporaryPassword = passwordGenerator.Generate();

        var graphResult = await usersService.EnablePasswordLogin(
            user.SubId.Value.ToString(),
            user.Name,
            user.Email,
            temporaryPassword,
            cancellationToken);

        return await graphResult.MatchAsync<Either<UserException, User>>(
            newSubId => PersistAndAudit(actorId, organisationId, user, before, temporaryPassword, newSubId, cancellationToken),
            e =>
            {
                logger.LogError(e, "EnablePasswordLogin failed for {UserId}", user.Id);
                return new UserUnknownException(user.Id, e);
            });
    }

    private async Task<Either<UserException, User>> PersistAndAudit(
        UserId actorId,
        OrganisationId? organisationId,
        User user,
        string before,
        string temporaryPassword,
        Option<string> newSubId,
        CancellationToken cancellationToken)
    {
        // Entra has already changed by now, so the row that mirrors it is committed first and on its own, before
        // anything that could roll it back. Inside one transaction with the revocation and the audit row, a failure in
        // either left users.sub_id pointing at an Entra account that no longer exists: no email went out, and every
        // retry called Entra with the dead id, so the user was locked out for good. No token for the same reason — a
        // dropped connection must not cancel the write that records what Entra has already done.
        User saved;
        try
        {
            // Set only when EnablePasswordLogin had to delete and recreate the Entra account (see
            // IUsersService.EnablePasswordLogin) — the old SubId stops resolving the moment that happened, so it
            // lands in the same UpdateAsync as EnablePasswordLogin(), not a separate follow-up write.
            newSubId.IfSome(id => user.UpdateSubId(new SubId(Guid.Parse(id)), dateTimeProvider.GetUtcNow()));

            user.EnablePasswordLogin(dateTimeProvider.GetUtcNow());
            saved = await repository.UpdateAsync(user, CancellationToken.None);
        }
        catch (Exception exception)
        {
            return new UserUnknownException(user.Id, exception);
        }

        // The session revocation and the audit row commit together (BLUM-8748); the email goes out only once they
        // have. A failure here answers 500 with the user row already matching Entra, so the admin's retry reaches
        // the live account and issues a fresh temporary password.
        var result = await transactionRunner.RunAsync<UserException, User>(
            async token =>
            {
                try
                {
                    await mfaSessionService.RevokeAllUserSessionsAsync(saved.Id);

                    await auditLogRepository.AppendAsync(
                        AuditLog.Create(actorId, AuditTargetType.User, saved.Id.Value, AuditAction.UserPasswordLoginEnabled, before, UserSnapshot.Serialize(saved), organisationId),
                        token);

                    return saved;
                }
                catch (Exception exception)
                {
                    return new UserUnknownException(user.Id, exception);
                }
            },
            cancellationToken);

        return await result.MatchAsync<Either<UserException, User>>(
            committed => SendTemporaryPassword(committed, temporaryPassword, cancellationToken),
            e => e);
    }

    private async Task<Either<UserException, User>> SendTemporaryPassword(
        User saved,
        string temporaryPassword,
        CancellationToken cancellationToken)
    {
        try
        {
            var emailResult = await temporaryPasswordEmailService.SendTemporaryPasswordAsync(
                saved.Email,
                new TemporaryPasswordNotification(saved.Name.ToString(), temporaryPassword),
                cancellationToken);

            // The change is committed either way — Entra and the session revocation already happened — but a
            // failed send must not be reported as success: it is the only way this user learns their new
            // password, so the caller needs to know it did not go out.
            return emailResult.Match<Either<UserException, User>>(
                _ => saved,
                e =>
                {
                    logger.LogWarning(e, "Temporary password email failed for {UserId}", saved.Id);
                    return new TemporaryPasswordEmailFailedException(saved.Id, e);
                });
        }
        catch (Exception exception)
        {
            return new UserUnknownException(saved.Id, exception);
        }
    }
}

public class EnableUserPasswordLoginCommandValidator : AbstractValidator<EnableUserPasswordLoginCommand>
{
    public EnableUserPasswordLoginCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
    }
}