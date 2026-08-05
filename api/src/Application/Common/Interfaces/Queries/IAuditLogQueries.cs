using Application.AuditLogs;
using Application.AuditLogs.Dtos;
using LanguageExt;

namespace Application.Common.Interfaces.Queries;

public interface IAuditLogQueries
{
    Task<PagedAuditLogs<AuditLogListItemDto>> GetPaged(AuditLogFilter filter, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<Option<AuditLogDetailDto>> GetById(Guid id, CancellationToken cancellationToken = default);
}