using Domain.Common;
using Domain.Organisations;
using Domain.Users;

namespace Domain.AuditLogs;

public class AuditLog : IEntity<AuditLogId>
{
    /// <summary>
    /// The version every Before/After shape started at (BLUM-8750). Each action's payload is versioned on its
    /// own: there is no global schema, because the ~40 shapes written today have nothing in common to version.
    /// </summary>
    public const int InitialPayloadVersion = 1;

    public AuditLogId Id { get; private set; }
    public UserId ActorUserId { get; private set; }
    public string TargetType { get; private set; }
    public Guid TargetId { get; private set; }
    public string Action { get; private set; }
    public string? Before { get; private set; }
    public string? After { get; private set; }
    public OrganisationId? OrganisationId { get; private set; }
    /// <summary>
    /// Not supplied by callers. AuditLogRepository.AppendAsync stamps it from IDateTimeProvider as the row is
    /// written; until then it is unset. A row added to the context without going through the repository falls
    /// back to the column default (the database's now()), so prefer the repository.
    /// </summary>
    public DateTime OccurredAt { get; private set; }

    /// <summary>
    /// Which shape of Before/After this row holds, read together with <see cref="Action"/>: version 2 of one
    /// action says nothing about another. A producer that changes the JSON it writes for an action passes the
    /// next version for that action only, so readers of older rows can still tell the two shapes apart. Rows
    /// written before the column existed read as <see cref="InitialPayloadVersion"/>.
    /// </summary>
    public int PayloadVersion { get; private set; }

    private AuditLog(
        AuditLogId id,
        UserId actorUserId,
        string targetType,
        Guid targetId,
        string action,
        string? before,
        string? after,
        OrganisationId? organisationId,
        DateTime occurredAt,
        int payloadVersion)
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
        PayloadVersion = payloadVersion;
    }

    public static AuditLog Create(
        UserId actorUserId,
        string targetType,
        Guid targetId,
        string action,
        string? before,
        string? after,
        OrganisationId? organisationId = null,
        int payloadVersion = InitialPayloadVersion)
    {
        Guard.NotNull(actorUserId, nameof(actorUserId));
        Guard.NotNullOrEmptyOrWhiteSpace(targetType, nameof(targetType));
        Guard.NotNullOrEmpty(action, nameof(action));
        Guard.GreaterThanOrEqual(payloadVersion, InitialPayloadVersion, nameof(payloadVersion));

        return new AuditLog(
            AuditLogId.New(),
            actorUserId,
            targetType,
            targetId,
            action,
            before,
            after,
            organisationId,
            occurredAt: default,
            payloadVersion);
    }
}