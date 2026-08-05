using Domain.DataCollectionForms;
using Domain.Submissions;
using Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Submissions;

public class SubmissionSectionConfiguration : IEntityTypeConfiguration<SubmissionSection>
{
    public void Configure(EntityTypeBuilder<SubmissionSection> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasConversion(x => x.Value, x => new SubmissionSectionId(x));
        builder.Property(x => x.SubmissionId).HasConversion(x => x.Value, x => new SubmissionId(x));

        // Id-only module reference; ModuleCode is the stable snapshot the task list keys on.
        builder.Property(x => x.FormModuleId).HasConversion(x => x.Value, x => new FormModuleId(x));
        builder.Property(x => x.ModuleCode).HasColumnType("varchar(100)").IsRequired();
        builder.Property(x => x.Status)
            .HasConversion(s => s.ToString(), v => SubmissionSectionStatus.From(v))
            .HasColumnType("varchar(32)")
            .IsRequired();
        builder.Property(x => x.ReviewRequired).IsRequired();
        builder.Property(x => x.UpdatedAt).HasConversion(new DateTimeUtcConverter());

        builder.HasIndex(x => new { x.SubmissionId, x.ModuleCode }).IsUnique();
    }
}