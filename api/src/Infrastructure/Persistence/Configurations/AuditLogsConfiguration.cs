using Domain.AuditLogs;
using Domain.Organisations;
using Domain.Users;
using Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public class AuditLogsConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasConversion(x => x.Value, x => new AuditLogId(x));
        builder.Property(x => x.ActorUserId).HasConversion(x => x.Value, x => new UserId(x));
        builder.Property(x => x.TargetType).HasColumnType("varchar(50)").IsRequired();
        builder.Property(x => x.TargetId).IsRequired();
        builder.Property(x => x.Action).HasColumnType("varchar(64)");
        builder.Property(x => x.Before).HasColumnType("jsonb").IsRequired(false);
        builder.Property(x => x.After).HasColumnType("jsonb").IsRequired(false);
        builder.Property(x => x.OrganisationId)
            .HasConversion(x => x!.Value, x => new OrganisationId(x))
            .IsRequired(false);
        builder.Property(x => x.OccurredAt)
            .HasConversion(new DateTimeUtcConverter())
            .HasDefaultValueSql("timezone('utc', now())");
        builder.Property(x => x.PayloadVersion)
            .IsRequired()
            .HasDefaultValue(AuditLog.InitialPayloadVersion);

        builder.HasIndex(x => new { x.TargetType, x.TargetId });
        builder.HasIndex(x => x.OccurredAt);
        builder.HasIndex(x => x.OrganisationId);
        builder.HasIndex(x => x.Action);

        // Serves the actor filter on the admin audit log, which orders by occurred_at (BLUM-8750).
        builder.HasIndex(x => new { x.ActorUserId, x.OccurredAt });
    }
}