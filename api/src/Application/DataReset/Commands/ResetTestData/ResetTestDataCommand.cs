using System.Text.Json;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Application.Common.Seeding;
using Application.DataReset.Exceptions;
using Domain.AuditLogs;
using Domain.Users;
using LanguageExt;
using MediatR;

namespace Application.DataReset.Commands.ResetTestData;

public record ResetTestDataCommand : IRequest<Either<DataResetException, ResetTestDataResult>>;

public record ResetTestDataResult;

public class ResetTestDataCommandHandler(IDataResetService dataResetService, IAuditLogRepository auditLogRepository)
    : IRequestHandler<ResetTestDataCommand, Either<DataResetException, ResetTestDataResult>>
{
    public async Task<Either<DataResetException, ResetTestDataResult>> Handle(
        ResetTestDataCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            await dataResetService.ResetToSeedDataAsync(cancellationToken);
            await AppendAuditAsync(succeeded: true, error: null);
            return new ResetTestDataResult();
        }
        catch (Exception exception)
        {
            // The reset may have truncated every table before failing, so the failure is audited too. A reseed that
            // failed names its failed steps, which every seeding failure now arrives as (BLUM-8749).
            var error = exception is SeedingIncompleteException incomplete
                ? $"{nameof(SeedingIncompleteException)}: {string.Join(", ", incomplete.FailedSteps)}"
                : exception.GetType().Name;
            await AppendAuditAsync(succeeded: false, error: error);
            return new DataResetUnknownException(exception);
        }
    }

    // Written after the reset, which truncates audit_logs: a row written before it would be wiped, so this becomes
    // the first row of the fresh log. No signed-in user (shared-secret endpoint), hence UserId.Empty(). The error is
    // the exception type, plus the failed step names for a seeding failure. It never includes the message, which can
    // carry SQL and row values and is already in the logs.
    private async Task AppendAuditAsync(bool succeeded, string? error)
    {
        await auditLogRepository.TryAppendAsync(
            AuditLog.Create(
                UserId.Empty(),
                AuditTargetType.System,
                Guid.Empty,
                AuditAction.SystemTestDataReset,
                before: null,
                after: JsonSerializer.Serialize(new { succeeded, error })));
    }
}