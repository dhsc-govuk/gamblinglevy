using System.Security.Cryptography;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Application.Common.Settings;
using Application.Mfa.Dtos;
using Application.Mfa.Exceptions;
using Domain.AuditLogs;
using Domain.Users;
using LanguageExt;
using MediatR;

namespace Application.Mfa.Commands;

public record VerifyMfaCommand(
    UserId UserId,
    string Code,
    string IpAddress,
    string UserAgent) : IRequest<Either<MfaException, MfaVerifyResponseDto>>;

public class VerifyMfaCommandHandler(
    ITotpService totpService,
    IMfaEncryptionService encryptionService,
    IMfaRateLimitService rateLimitService,
    IMfaSessionService sessionService,
    IUserMfaRepository userMfaRepository,
    IAuditLogRepository auditLogRepository,
    MfaAttemptGate attemptGate,
    MfaFailureRecorder failureRecorder,
    IDateTimeProvider dateTimeProvider,
    ITransactionRunner transactionRunner,
    ApplicationSettings settings)
    : IRequestHandler<VerifyMfaCommand, Either<MfaException, MfaVerifyResponseDto>>
{
    public async Task<Either<MfaException, MfaVerifyResponseDto>> Handle(
        VerifyMfaCommand request,
        CancellationToken cancellationToken)
    {
        var refusal = await attemptGate.ReserveAsync(request.UserId, cancellationToken);
        if (refusal is not null)
        {
            return refusal;
        }

        var userMfaOption = await userMfaRepository.GetByUserIdAsync(request.UserId, cancellationToken);

        return await userMfaOption.MatchAsync(
            mfa => ValidateAndVerifyAsync(request, mfa, cancellationToken),
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
            ? new MfaLockedOutException(userId, mfa.LockedUntil!.Value, RetryAfterSeconds(mfa, utcNow))
            : mfa;
    }

    private int RetryAfterSeconds(UserMfa mfa, DateTime utcNow)
    {
        return MfaRetryAfter.Seconds(mfa.LockedUntil, utcNow, settings.Mfa.LockoutMinutes * 60);
    }

    private async Task<Either<MfaException, MfaVerifyResponseDto>> ValidateAndVerifyAsync(
        VerifyMfaCommand request,
        UserMfa mfa,
        CancellationToken cancellationToken)
    {
        return await ValidateIsEnabled(mfa, request.UserId)
            .Bind(m => ValidateNotLockedOut(m, request.UserId))
            .BindAsync(m => ValidateCodeAndCreateSessionAsync(request, m, cancellationToken));
    }

    private async Task<Either<MfaException, MfaVerifyResponseDto>> ValidateCodeAndCreateSessionAsync(
        VerifyMfaCommand request,
        UserMfa mfa,
        CancellationToken cancellationToken)
    {
        // A code outside the six-digit TOTP alphabet cannot validate, so it fails before the secret is
        // decrypted. It still takes the same branch as a wrong code rather than returning early: refusing
        // to count malformed input would leave the per-minute limiter and the lockout counter blind to a
        // burst of it, and would remove the mfa.verify.failed trail this ticket exists to strengthen
        // (BLUM-7240).
        if (!MfaCodeFormat.IsTotpShape(request.Code))
        {
            return await RecordFailedAttemptAsync(request, mfa, cancellationToken);
        }

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

        if (!totpService.ValidateCode(secret, request.Code))
        {
            return await RecordFailedAttemptAsync(request, mfa, cancellationToken);
        }

        rateLimitService.ResetAttempts(request.UserId);

        // The counter reset and its audit row commit together (BLUM-8748); the session is created once they have.
        await transactionRunner.RunAsync(
            async token =>
            {
                mfa.ResetFailedAttempts(dateTimeProvider.GetUtcNow());
                await userMfaRepository.UpdateAsync(mfa, token);

                await auditLogRepository.AppendAsync(
                    AuditLog.Create(request.UserId, AuditTargetType.User, request.UserId.Value, AuditAction.MfaVerifySucceeded, before: null, after: null),
                    token);
            },
            cancellationToken);

        var session = await sessionService.CreateSessionAsync(
            request.UserId,
            request.IpAddress,
            request.UserAgent);

        return new MfaVerifyResponseDto(true, session.Id.Value, session.ExpiresAt);
    }

    private async Task<Either<MfaException, MfaVerifyResponseDto>> RecordFailedAttemptAsync(
        VerifyMfaCommand request,
        UserMfa mfa,
        CancellationToken cancellationToken)
    {
        // The attempt was already counted against the per-minute damper by Reserve, whether it turned out to
        // be right or wrong — that is what makes it a damper on guessing rather than a damper on failing.
        return await failureRecorder.RecordAsync(
            request.UserId,
            mfa,
            AuditAction.MfaVerifyFailed,
            MfaEndpoints.Verify,
            new InvalidMfaCodeException(request.UserId));
    }
}