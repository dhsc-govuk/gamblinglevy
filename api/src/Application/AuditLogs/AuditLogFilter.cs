namespace Application.AuditLogs;

public record AuditLogFilter(
    string? Action,
    Guid? ActorUserId,
    Guid? TargetUserId,
    Guid? OrganisationId,
    DateTime? FromUtc,
    DateTime? ToUtc,
    string? Sort);

public record PagedAuditLogs<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);