using Domain.Activities;
using Domain.Submissions;
using Domain.Users;
using Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Submissions;

public class SubmissionActivityReportConfiguration : IEntityTypeConfiguration<SubmissionActivityReport>
{
    public void Configure(EntityTypeBuilder<SubmissionActivityReport> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasConversion(x => x.Value, x => new SubmissionActivityReportId(x));

        // Id-only references (no FK / navigation) — mirrors ActivityAnswer so rows persist through their own
        // repository without tracking conflicts against the Submission graph.
        builder.Property(x => x.SubmissionId).HasConversion(x => x.Value, x => new SubmissionId(x));
        builder.Property(x => x.ActivityId).HasConversion(x => x.Value, x => new ActivityId(x));
        builder.Property(x => x.Status)
            .HasConversion(s => s.ToString(), v => SubmissionSectionStatus.From(v))
            .HasColumnType("varchar(32)")
            .IsRequired();
        builder.Property(x => x.CreatedBy).HasConversion(x => x.Value, x => new UserId(x));
        builder.Property(x => x.CreatedAt).HasConversion(new DateTimeUtcConverter());
        builder.Property(x => x.UpdatedAt).HasConversion(new DateTimeUtcConverter());

        builder.HasIndex(x => new { x.SubmissionId, x.ActivityId }).IsUnique();
    }
}