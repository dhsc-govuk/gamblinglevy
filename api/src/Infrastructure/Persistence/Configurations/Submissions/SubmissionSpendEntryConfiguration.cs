using Domain.Activities;
using Domain.Submissions;
using Domain.Users;
using Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Submissions;

public class SubmissionSpendEntryConfiguration : IEntityTypeConfiguration<SubmissionSpendEntry>
{
    public void Configure(EntityTypeBuilder<SubmissionSpendEntry> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasConversion(x => x.Value, x => new SubmissionSpendEntryId(x));

        // Id-only references — spend rows persist through their own repository without tracking conflicts against
        // the Submission graph. A null ActivityId is the non-activity "other" line.
        builder.Property(x => x.SubmissionId).HasConversion(x => x.Value, x => new SubmissionId(x));
        builder.Property(x => x.ActivityId)
            .HasConversion(x => x == null ? (Guid?)null : x.Value, x => x == null ? null : new ActivityId(x.Value));
        builder.Property(x => x.Kind)
            .HasConversion(k => k.ToString(), v => SpendEntryKind.From(v))
            .HasColumnType("varchar(16)")
            .IsRequired();
        builder.Property(x => x.Quarter);
        builder.Property(x => x.Amount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(x => x.CreatedBy).HasConversion(x => x.Value, x => new UserId(x));
        builder.Property(x => x.CreatedAt).HasConversion(new DateTimeUtcConverter());

        builder.HasIndex(x => new { x.SubmissionId, x.Kind });

        // Backstop against duplicate Forecast activity rows only. Postgres treats NULL as distinct, so it constrains
        // neither the "other" lines (NULL ActivityId) nor any Actual row (NULL Quarter). The real guarantee is the
        // wholesale replace, serialised per submission by the row lock in ReplaceForSubmissionAsync.
        builder.HasIndex(x => new { x.SubmissionId, x.ActivityId, x.Kind, x.Quarter }).IsUnique();
    }
}