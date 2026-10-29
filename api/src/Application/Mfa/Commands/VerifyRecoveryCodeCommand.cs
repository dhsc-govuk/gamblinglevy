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

public record VerifyRecoveryCodeCommand(
    UserId UserId,
    string RecoveryCode,
    string IpAddress,
    string UserAgent) : IRequest<Either<MfaException, MfaVerifyResponseDto>>;

public class VerifyRecoveryCodeCommandHandler(
    IRecoveryCodeService recoveryCodeService,
    IMfaSessionService sessionService,
    IMfaRateLimitService rateLimitService,
    IUserMfaRepository userMfaRepository,
    IAuditLogRepository auditLogRepository,
    MfaAttemptGate attemptGate,
    MfaFailureRecorder failureRecorder,
    IDateTimeProvider dateTimeProvider,
    ITransactionRunner transactionRunner,
    ApplicationSettings settings)
    : IRequestHandler<VerifyRecoveryCodeCommand, Either<MfaException, MfaVerifyResponseDto>>
{
    public async Task<Either<MfaException, MfaVerifyResponseDto>> Handle(
        VerifyRecoveryCodeCommand request,
        CancellationToken cancellationToken)
    {
        // This endpoint had no damper at all until BLUM-7239, and it is the most expensive one in the API to
        // answer wrongly: BCrypt at cost 12 against every stored hash, roughly two and a half CPU-seconds a
        // request. Unlimited, it is a denial-of-service vector on its own, quite apart from being a guessing
        // oracle against a single-use bypass of the authenticator.
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

        // The lock deliberately covers this path too. A recovery code is guessable in exactly the way a TOTP
        // code is, so leaving the escape hatch open during a lockout would mean the lockout could simply be
        // walked around. The cost of that decision — an administrator's mfa reset becomes the only way back
        // for somebody genuinely locked out — is why RecordFailedAttempt now clears an expired lock.
        return mfa.IsLockedOut(utcNow)
            ? new MfaLockedOutException(userId, mfa.LockedUntil!.Value, RetryAfterSeconds(mfa, utcNow))
            : mfa;
    }

    private int RetryAfterSeconds(UserMfa mfa, DateTime utcNow)
    {
        return MfaRetryAfter.Seconds(mfa.LockedUntil, utcNow, settings.Mfa.LockoutMinutes * 60);
    }

    private async Task<Either<MfaException, MfaVerifyResponseDto>> ValidateAndVerifyAsync(
        VerifyRecoveryCodeCommand request,
        UserMfa mfa,
        CancellationToken cancellationToken)
    {
        return await ValidateIsEnabled(mfa, request.UserId)
            .Bind(m => ValidateNotLockedOut(m, request.UserId))
            .BindAsync(m => ValidateRecoveryCodeAndCreateSessionAsync(request, m, cancellationToken));
    }

    private async Task<Either<MfaException, MfaVerifyResponseDto>> ValidateRecoveryCodeAndCreateSessionAsync(
        VerifyRecoveryCodeCommand request,
        UserMfa mfa,
        CancellationToken cancellationToken)
    {
        // Checked before the loop below, which is the most expensive thing this API does on an
        // unauthenticated-ish path: BCrypt at cost 12 against every stored hash, roughly a quarter of a
        // second each, and this endpoint has no per-minute limiter. A code that is not twelve alphanumeric
        // characters cannot match any of them, so there is nothing to gain by hashing it — but it still
        // takes the same failure branch, so the lockout counter keeps counting (BLUM-7240).
        if (!MfaCodeFormat.IsRecoveryCodeShape(request.RecoveryCode))
        {
            return await RecordFailedAttemptAsync(request, mfa, cancellationToken);
        }

        string? matchedHashedCode = null;
        foreach (var hashedCode in mfa.HashedRecoveryCodes)
        {
            if (recoveryCodeService.VerifyRecoveryCode(request.RecoveryCode, hashedCode))
            {
                matchedHashedCode = hashedCode;
                break;
            }
        }

        if (matchedHashedCode == null)
        {
            return await RecordFailedAttemptAsync(request, mfa, cancellationToken);
        }

        rateLimitService.ResetAttempts(request.UserId);
        string spentCode = matchedHashedCode;

        // The spent code, the counter reset and the audit row commit together (BLUM-8748); the session is created
        // once they have.
        await transactionRunner.RunAsync(
            async token =>
            {
                mfa.RemoveRecoveryCode(spentCode, dateTimeProvider.GetUtcNow());
                mfa.ResetFailedAttempts(dateTimeProvider.GetUtcNow());
                await userMfaRepository.UpdateAsync(mfa, token);

                await auditLogRepository.AppendAsync(
                    AuditLog.Create(request.UserId, AuditTargetType.User, request.UserId.Value, AuditAction.MfaRecoveryCodeUsed, before: null, after: null),
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
        VerifyRecoveryCodeCommand request,
        UserMfa mfa,
        CancellationToken cancellationToken)
    {
        // Until BLUM-7239 this path wrote no audit row at all, so a brute force against the recovery codes
        // was invisible while the equivalent attack on TOTP was recorded.
        return await failureRecorder.RecordAsync(
            request.UserId,
            mfa,
            AuditAction.MfaRecoveryCodeFailed,
            MfaEndpoints.VerifyRecovery,
            new InvalidRecoveryCodeException(request.UserId));
    }
}