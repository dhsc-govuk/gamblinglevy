using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
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
    IUserMfaRepository userMfaRepository,
    IAuditLogRepository auditLogRepository)
    : IRequestHandler<VerifyRecoveryCodeCommand, Either<MfaException, MfaVerifyResponseDto>>
{
    public async Task<Either<MfaException, MfaVerifyResponseDto>> Handle(
        VerifyRecoveryCodeCommand request,
        CancellationToken cancellationToken)
    {
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
        return mfa.IsLockedOut()
            ? new MfaRateLimitException(userId, mfa.LockedUntil)
            : mfa;
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

        mfa.RemoveRecoveryCode(matchedHashedCode);
        mfa.ResetFailedAttempts();
        await userMfaRepository.UpdateAsync(mfa, cancellationToken);

        await auditLogRepository.AppendAsync(
            AuditLog.Create(request.UserId, AuditTargetType.User, request.UserId.Value, AuditAction.MfaRecoveryCodeUsed, before: null, after: null),
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
        mfa.RecordFailedAttempt();
        await userMfaRepository.UpdateAsync(mfa, cancellationToken);
        return new InvalidRecoveryCodeException(request.UserId);
    }
}