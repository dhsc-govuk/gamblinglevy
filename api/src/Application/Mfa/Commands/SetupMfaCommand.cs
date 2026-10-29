using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Application.Mfa.Dtos;
using Application.Mfa.Exceptions;
using Domain.AuditLogs;
using Domain.Users;
using LanguageExt;
using MediatR;

namespace Application.Mfa.Commands;

public record SetupMfaCommand(UserId UserId, string Email) : IRequest<Either<MfaException, MfaSetupResponseDto>>;

public class SetupMfaCommandHandler(
    ITotpService totpService,
    IMfaEncryptionService encryptionService,
    IUserMfaRepository userMfaRepository,
    IAuditLogRepository auditLogRepository,
    IDateTimeProvider dateTimeProvider,
    ITransactionRunner transactionRunner)
    : IRequestHandler<SetupMfaCommand, Either<MfaException, MfaSetupResponseDto>>
{
    public async Task<Either<MfaException, MfaSetupResponseDto>> Handle(
        SetupMfaCommand request,
        CancellationToken cancellationToken)
    {
        var existingMfaOption = await userMfaRepository.GetByUserIdAsync(request.UserId, cancellationToken);

        return await existingMfaOption.MatchAsync(
            mfa => UpdateExistingSetup(mfa, request, cancellationToken),
            () => CreateNewSetup(request, cancellationToken));
    }

    private async Task<Either<MfaException, MfaSetupResponseDto>> UpdateExistingSetup(
        UserMfa mfa,
        SetupMfaCommand request,
        CancellationToken cancellationToken)
    {
        if (mfa.IsEnabled)
        {
            return new MfaAlreadyEnabledException(request.UserId);
        }

        var secret = totpService.GenerateSecret();
        var qrCodeUri = totpService.GenerateQrCodeUri(request.Email, secret);
        var encryptedSecret = encryptionService.Encrypt(secret);

        // The new secret and its audit row commit together (BLUM-8748).
        await transactionRunner.RunAsync(
            async token =>
            {
                mfa.UpdateSecret(encryptedSecret, dateTimeProvider.GetUtcNow());
                await userMfaRepository.UpdateAsync(mfa, token);
                await AppendAuditAsync(request.UserId, token);
            },
            cancellationToken);

        return new MfaSetupResponseDto(qrCodeUri, secret);
    }

    private async Task<Either<MfaException, MfaSetupResponseDto>> CreateNewSetup(
        SetupMfaCommand request,
        CancellationToken cancellationToken)
    {
        var secret = totpService.GenerateSecret();
        var qrCodeUri = totpService.GenerateQrCodeUri(request.Email, secret);
        var encryptedSecret = encryptionService.Encrypt(secret);

        // The pending enrolment and its audit row commit together (BLUM-8748).
        await transactionRunner.RunAsync(
            async token =>
            {
                var userMfa = UserMfa.New(UserMfaId.New(), request.UserId, encryptedSecret, dateTimeProvider.GetUtcNow());
                await userMfaRepository.AddAsync(userMfa, token);
                await AppendAuditAsync(request.UserId, token);
            },
            cancellationToken);

        return new MfaSetupResponseDto(qrCodeUri, secret);
    }

    // Before and after stay null: the only state this step creates is the TOTP secret and its QR URI.
    private Task AppendAuditAsync(UserId userId, CancellationToken cancellationToken) =>
        auditLogRepository.AppendAsync(
            AuditLog.Create(userId, AuditTargetType.User, userId.Value, AuditAction.MfaSetupStarted, before: null, after: null),
            cancellationToken);
}