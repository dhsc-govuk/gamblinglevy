using System.Security.Cryptography;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Application.Common.Settings;
using Application.Mfa.Exceptions;
using Domain.AuditLogs;
using Domain.Users;
using LanguageExt;
using MediatR;

namespace Application.Mfa.Commands;

public record DisableMfaCommand(UserId UserId, string Code) : IRequest<Either<MfaException, bool>>;

public class DisableMfaCommandHandler(
    ITotpService totpService,
    IMfaEncryptionService encryptionService,
    IMfaSessionService sessionService,
    IMfaRateLimitService rateLimitService,
    IUserMfaRepository userMfaRepository,
    IAuditLogRepository auditLogRepository,
    MfaAttemptGate attemptGate,
    MfaFailureRecorder failureRecorder,
    IDateTimeProvider dateTimeProvider,
    ITransactionRunner transactionRunner,
    ApplicationSettings settings)
    : IRequestHandler<DisableMfaCommand, Either<MfaException, bool>>
{
    public async Task<Either<MfaException, bool>> Handle(
        DisableMfaCommand request,
        CancellationToken cancellationToken)
    {
        // Guessing here turns MFA off outright, so leaving it uncounted would have made the lock on
        // /mfa/verify pointless — an attacker would simply guess at this endpoint instead.
        var refusal = await attemptGate.ReserveAsync(request.UserId, cancellationToken);
        if (refusal is not null)
        {
            return refusal;
        }

        var userMfaOption = await userMfaRepository.GetByUserIdAsync(request.UserId, cancellationToken);

        return await userMfaOption.MatchAsync(
            mfa => ValidateAndDisableAsync(request, mfa, cancellationToken),
            () => new MfaNotEnabledException(request.UserId));
    }

    private Either<MfaException, UserMfa> ValidateIsEnabled(UserMfa mfa, UserId userId)
    {
        return mfa.IsEnabled
            ? mfa
            : new MfaNotEnabledException(userId);
    }

    private Either<MfaException, UserMfa> ValidateNotLockedOut(UserMfa mfa, UserId userId)
    {
        var utcNow = dateTimeProvider.GetUtcNow();

        return mfa.IsLockedOut(utcNow)
            ? new MfaLockedOutException(
                userId,
                mfa.LockedUntil!.Value,
                MfaRetryAfter.Seconds(mfa.LockedUntil, utcNow, settings.Mfa.LockoutMinutes * 60))
            : mfa;
    }

    private async Task<Either<MfaException, UserMfa>> ValidateCodeAsync(
        UserMfa mfa,
        DisableMfaCommand request,
        CancellationToken cancellationToken)
    {
        string secret;
        try
        {
            secret = encryptionService.Decrypt(mfa.EncryptedSecret);
        }
        catch (CryptographicException ex)
        {
            // A lost or rotated Data Protection key (see IMfaEncryptionService.Decrypt) would otherwise
            // surface as an unhandled 500 with no MFA_* envelope — UnhandledExceptionBehaviour rethrows
            // anything that is not part of this handler's Either.
            return new MfaUnknownException(request.UserId, ex);
        }

        if (totpService.ValidateCode(secret, request.Code))
        {
            rateLimitService.ResetAttempts(request.UserId);
            return mfa;
        }

        return await failureRecorder.RecordAsync(
            request.UserId,
            mfa,
            AuditAction.MfaVerifyFailed,
            MfaEndpoints.Disable,
            new InvalidMfaCodeException(request.UserId));
    }

    private async Task<Either<MfaException, bool>> ValidateAndDisableAsync(
        DisableMfaCommand request,
        UserMfa mfa,
        CancellationToken cancellationToken)
    {
        return await ValidateIsEnabled(mfa, request.UserId)
            .Bind(m => ValidateNotLockedOut(m, request.UserId))
            .BindAsync(m => ValidateCodeAsync(m, request, cancellationToken))
            .BindAsync(m => DisableMfaAsync(m, request.UserId, cancellationToken));
    }

    private async Task<Either<MfaException, bool>> DisableMfaAsync(
        UserMfa mfa,
        UserId userId,
        CancellationToken cancellationToken)
    {
        // The disable, the session revocation and the audit row commit together (BLUM-8748).
        return await transactionRunner.RunAsync<MfaException, bool>(
            async token =>
            {
                mfa.Disable(dateTimeProvider.GetUtcNow());
                await userMfaRepository.UpdateAsync(mfa, token);
                await sessionService.RevokeAllUserSessionsAsync(userId);

                await auditLogRepository.AppendAsync(
                    AuditLog.Create(userId, AuditTargetType.User, userId.Value, AuditAction.MfaDisabled, before: null, after: null),
                    token);

                return true;
            },
            cancellationToken);
    }
}