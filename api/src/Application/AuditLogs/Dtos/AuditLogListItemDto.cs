namespace Application.AuditLogs.Dtos;

public record AuditLogListItemDto(
    Guid Id,
    DateTime OccurredAt,
    string Action,
    Guid ActorUserId,
    string? ActorDisplayName,
    string TargetType,
    Guid TargetId,
    string? TargetDisplayName,
    Guid? OrganisationId,
    string? OrganisationName);

public record AuditLogDetailDto(
    Guid Id,
    DateTime OccurredAt,
    string Action,
    Guid ActorUserId,
    string? ActorDisplayName,
    string TargetType,
    Guid TargetId,
    string? TargetDisplayName,
    Guid? OrganisationId,
    string? OrganisationName,
    string? Before,
    string? After,
    int PayloadVersion);