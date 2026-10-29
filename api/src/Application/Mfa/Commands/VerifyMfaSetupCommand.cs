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

public record VerifyMfaSetupCommand(UserId UserId, string Code, string IpAddress, string UserAgent)
    : IRequest<Either<MfaException, MfaSetupCompleteResponseDto>>;

public class VerifyMfaSetupCommandHandler(
    ITotpService totpService,
    IMfaEncryptionService encryptionService,
    IRecoveryCodeService recoveryCodeService,
    IMfaRateLimitService rateLimitService,
    IUserMfaRepository userMfaRepository,
    IMfaSessionService sessionService,
    IAuditLogRepository auditLogRepository,
    MfaAttemptGate attemptGate,
    MfaFailureRecorder failureRecorder,
    IDateTimeProvider dateTimeProvider,
    ITransactionRunner transactionRunner,
    ApplicationSettings settings)
    : IRequestHandler<VerifyMfaSetupCommand, Either<MfaException, MfaSetupCompleteResponseDto>>
{
    public async Task<Either<MfaException, MfaSetupCompleteResponseDto>> Handle(
        VerifyMfaSetupCommand request,
        CancellationToken cancellationToken)
    {
        // Before BLUM-7239 this endpoint counted nothing whatsoever — no damper and no lockout — so it was
        // an unbounded oracle against the pending secret. Somebody holding a stolen token for a user who was
        // midway through enrolment could brute-force the code and complete enrolment on their behalf, ending
        // up with the authenticator.
        //
        // It gets both, like the other four. The lockout half is enforced further down, once the row has
        // been loaded — see the comment on IsLockedOut in ValidateAndCompleteSetupAsync for why a pending
        // enrolment is worth locking.
        var refusal = await attemptGate.ReserveAsync(request.UserId, cancellationToken);
        if (refusal is not null)
        {
            return refusal;
        }

        var userMfaOption = await userMfaRepository.GetByUserIdAsync(request.UserId, cancellationToken);

        return await userMfaOption.MatchAsync(
            mfa => ValidateAndCompleteSetupAsync(mfa, request, cancellationToken),
            () => Task.FromResult<Either<MfaException, MfaSetupCompleteResponseDto>>(
                new MfaSetupExpiredException(request.UserId)));
    }

    private async Task<Either<MfaException, MfaSetupCompleteResponseDto>> ValidateAndCompleteSetupAsync(
        UserMfa mfa,
        VerifyMfaSetupCommand request,
        CancellationToken cancellationToken)
    {
        var utcNow = dateTimeProvider.GetUtcNow();

        if (mfa.IsEnabled)
        {
            return new MfaAlreadyEnabledException(request.UserId);
        }

        if (string.IsNullOrEmpty(mfa.EncryptedSecret))
        {
            return new MfaSetupExpiredException(request.UserId);
        }

        // The lock covers this endpoint too. Guessing here is guessing at a real secret — one that, once
        // enrolled, is the second factor — so an attacker holding a stolen token for a user who is mid-setup
        // has the same oracle as anywhere else, and leaving it out would make the lock avoidable by simply
        // attacking whoever has not finished enrolling yet.
        if (mfa.IsLockedOut(utcNow))
        {
            return new MfaLockedOutException(
                request.UserId,
                mfa.LockedUntil!.Value,
                MfaRetryAfter.Seconds(mfa.LockedUntil, utcNow, settings.Mfa.LockoutMinutes * 60));
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
            // Until this was added, the one endpoint whose success hands the attacker the authenticator was
            // also the only one that recorded nothing: no audit row and no increment of the persistent
            // counter, so a brute force against a pending enrolment was invisible and unbounded except by an
            // in-memory damper that resets whenever the App Service unloads.
            return await failureRecorder.RecordAsync(
                request.UserId,
                mfa,
                AuditAction.MfaVerifyFailed,
                MfaEndpoints.SetupVerify,
                new InvalidMfaCodeException(request.UserId));
        }

        rateLimitService.ResetAttempts(request.UserId);

        var recoveryCodes = recoveryCodeService.GenerateRecoveryCodes();
        var hashedCodes = recoveryCodes.Select(recoveryCodeService.HashRecoveryCode).ToList();

        // The enrolment and its audit row commit together (BLUM-8748); the session is created once they have.
        await transactionRunner.RunAsync(
            async token =>
            {
                // Read again rather than reusing the instant the lock was judged against: hashing the recovery codes
                // above is deliberately slow, and EnabledAt records when enrolment completed.
                mfa.Enable(hashedCodes, dateTimeProvider.GetUtcNow());
                await userMfaRepository.UpdateAsync(mfa, token);

                await auditLogRepository.AppendAsync(
                    AuditLog.Create(request.UserId, AuditTargetType.User, request.UserId.Value, AuditAction.MfaEnrolled, before: null, after: null),
                    token);
            },
            cancellationToken);

        var session = await sessionService.CreateSessionAsync(
            request.UserId,
            request.IpAddress,
            request.UserAgent);

        return new MfaSetupCompleteResponseDto(true, recoveryCodes, session.Id.Value, session.ExpiresAt);
    }
}