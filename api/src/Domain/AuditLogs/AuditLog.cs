using Domain.Common;
using Domain.Organisations;
using Domain.Users;

namespace Domain.AuditLogs;

public class AuditLog : IEntity<AuditLogId>
{
    public AuditLogId Id { get; private set; }
    public UserId ActorUserId { get; private set; }
    public string TargetType { get; private set; }
    public Guid TargetId { get; private set; }
    public string Action { get; private set; }
    public string? Before { get; private set; }
    public string? After { get; private set; }
    public OrganisationId? OrganisationId { get; private set; }
    public DateTime OccurredAt { get; private set; }

    private AuditLog(
        AuditLogId id,
        UserId actorUserId,
        string targetType,
        Guid targetId,
        string action,
        string? before,
        string? after,
        OrganisationId? organisationId,
        DateTime occurredAt)
    {
        Id = id;
        ActorUserId = actorUserId;
        TargetType = targetType;
        TargetId = targetId;
        Action = action;
        Before = before;
        After = after;
        OrganisationId = organisationId;
        OccurredAt = occurredAt;
    }

    public static AuditLog Create(
        UserId actorUserId,
        string targetType,
        Guid targetId,
        string action,
        string? before,
        string? after,
        OrganisationId? organisationId = null)
    {
        Guard.NotNull(actorUserId, nameof(actorUserId));
        Guard.NotNullOrEmptyOrWhiteSpace(targetType, nameof(targetType));
        Guard.NotNullOrEmpty(action, nameof(action));

        return new AuditLog(
            AuditLogId.New(),
            actorUserId,
            targetType,
            targetId,
            action,
            before,
            after,
            organisationId,
            DateTime.UtcNow);
    }
}