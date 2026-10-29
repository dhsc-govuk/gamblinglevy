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

public record RegenerateRecoveryCodesCommand(UserId UserId, string Code) : IRequest<Either<MfaException, MfaRecoveryCodesDto>>;

public class RegenerateRecoveryCodesCommandHandler(
    ITotpService totpService,
    IMfaEncryptionService encryptionService,
    IRecoveryCodeService recoveryCodeService,
    IMfaRateLimitService rateLimitService,
    IUserMfaRepository userMfaRepository,
    MfaAttemptGate attemptGate,
    MfaFailureRecorder failureRecorder,
    IDateTimeProvider dateTimeProvider,
    ApplicationSettings settings)
    : IRequestHandler<RegenerateRecoveryCodesCommand, Either<MfaException, MfaRecoveryCodesDto>>
{
    public async Task<Either<MfaException, MfaRecoveryCodesDto>> Handle(
        RegenerateRecoveryCodesCommand request,
        CancellationToken cancellationToken)
    {
        // Guessing here mints a fresh set of recovery codes and hands them to the caller, which is a
        // complete takeover of the second factor — so it is counted like any other code-taking endpoint.
        var refusal = await attemptGate.ReserveAsync(request.UserId, cancellationToken);
        if (refusal is not null)
        {
            return refusal;
        }

        var userMfaOption = await userMfaRepository.GetByUserIdAsync(request.UserId, cancellationToken);

        return await userMfaOption.MatchAsync(
            mfa => ValidateAndRegenerateAsync(request, mfa, cancellationToken),
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
        RegenerateRecoveryCodesCommand request,
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
            MfaEndpoints.RegenerateRecoveryCodes,
            new InvalidMfaCodeException(request.UserId));
    }

    private async Task<Either<MfaException, MfaRecoveryCodesDto>> ValidateAndRegenerateAsync(
        RegenerateRecoveryCodesCommand request,
        UserMfa mfa,
        CancellationToken cancellationToken)
    {
        return await ValidateIsEnabled(mfa, request.UserId)
            .Bind(m => ValidateNotLockedOut(m, request.UserId))
            .BindAsync(m => ValidateCodeAsync(m, request, cancellationToken))
            .BindAsync(m => RegenerateCodesAsync(m, cancellationToken));
    }

    private async Task<Either<MfaException, MfaRecoveryCodesDto>> RegenerateCodesAsync(
        UserMfa mfa,
        CancellationToken cancellationToken)
    {
        var recoveryCodes = recoveryCodeService.GenerateRecoveryCodes();
        var hashedCodes = recoveryCodes.Select(recoveryCodeService.HashRecoveryCode).ToList();

        mfa.UpdateRecoveryCodes(hashedCodes, dateTimeProvider.GetUtcNow());
        await userMfaRepository.UpdateAsync(mfa, cancellationToken);

        return new MfaRecoveryCodesDto(recoveryCodes);
    }
}