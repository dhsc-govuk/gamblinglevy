using System.Text.Json;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Application.DataReset.Exceptions;
using Domain.AuditLogs;
using Domain.Users;
using LanguageExt;
using MediatR;

namespace Application.DataReset.Commands.ClearMfaData;

public record ClearMfaDataCommand : IRequest<Either<DataResetException, ClearMfaDataResult>>;

public record ClearMfaDataResult;

public class ClearMfaDataCommandHandler(
    IDataResetService dataResetService,
    IAuditLogRepository auditLogRepository,
    ITransactionRunner transactionRunner)
    : IRequestHandler<ClearMfaDataCommand, Either<DataResetException, ClearMfaDataResult>>
{
    public async Task<Either<DataResetException, ClearMfaDataResult>> Handle(
        ClearMfaDataCommand request,
        CancellationToken cancellationToken)
    {
        // The two deletes and their audit row commit together (BLUM-8748); ClearMfaDataAsync joins this transaction.
        return await transactionRunner.RunAsync<DataResetException, ClearMfaDataResult>(
            async token =>
            {
                try
                {
                    var cleared = await dataResetService.ClearMfaDataAsync(token);

                    // No signed-in user: the endpoint is gated by a shared secret, so the actor is UserId.Empty().
                    await auditLogRepository.AppendAsync(
                        AuditLog.Create(
                            UserId.Empty(),
                            AuditTargetType.System,
                            Guid.Empty,
                            AuditAction.SystemMfaDataCleared,
                            before: null,
                            after: JsonSerializer.Serialize(new
                            {
                                userMfasDeleted = cleared.UserMfasDeleted,
                                sessionsDeleted = cleared.SessionsDeleted,
                            })),
                        token);

                    return new ClearMfaDataResult();
                }
                catch (Exception exception)
                {
                    return new MfaDataClearUnknownException(exception);
                }
            },
            cancellationToken);
    }
}