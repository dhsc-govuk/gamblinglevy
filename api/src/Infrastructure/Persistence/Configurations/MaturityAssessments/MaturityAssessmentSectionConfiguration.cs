using Domain.MaturityAssessments;
using Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.MaturityAssessments;

public class MaturityAssessmentSectionConfiguration : IEntityTypeConfiguration<MaturityAssessmentSection>
{
    public void Configure(EntityTypeBuilder<MaturityAssessmentSection> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasConversion(x => x.Value, x => new MaturityAssessmentSectionId(x));
        builder.Property(x => x.AssessmentId).HasConversion(x => x.Value, x => new MaturityAssessmentId(x));
        builder.Property(x => x.SectionCode).HasColumnType("varchar(100)").IsRequired();
        builder.Property(x => x.Status)
            .HasConversion(s => s.ToString(), v => MaturityAssessmentSectionStatus.From(v))
            .HasColumnType("varchar(32)")
            .IsRequired();
        builder.Property(x => x.UpdatedAt).HasConversion(new DateTimeUtcConverter());

        builder.HasIndex(x => new { x.AssessmentId, x.SectionCode }).IsUnique();
    }
}