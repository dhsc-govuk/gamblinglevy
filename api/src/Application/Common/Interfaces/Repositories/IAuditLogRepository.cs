using Domain.AuditLogs;

namespace Application.Common.Interfaces.Repositories;

public interface IAuditLogRepository
{
    Task<AuditLog> AppendAsync(AuditLog entry, CancellationToken cancellationToken = default);
}