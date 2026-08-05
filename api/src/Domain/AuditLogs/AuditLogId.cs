namespace Domain.AuditLogs;

public record AuditLogId(Guid Value)
{
    public static AuditLogId Empty() => new(Guid.Empty);
    public static AuditLogId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}