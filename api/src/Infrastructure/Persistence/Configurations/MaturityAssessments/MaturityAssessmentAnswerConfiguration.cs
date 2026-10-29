using Domain.MaturityAssessments;
using Domain.Users;
using Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.MaturityAssessments;

public class MaturityAssessmentAnswerConfiguration : IEntityTypeConfiguration<MaturityAssessmentAnswer>
{
    public void Configure(EntityTypeBuilder<MaturityAssessmentAnswer> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasConversion(x => x.Value, x => new MaturityAssessmentAnswerId(x));

        // Id-only reference — answers are replaced per question code through their own repository.
        builder.Property(x => x.AssessmentId).HasConversion(x => x.Value, x => new MaturityAssessmentId(x));
        builder.Property(x => x.QuestionCode).HasColumnType("varchar(100)").IsRequired();
        builder.Property(x => x.Value).HasColumnType("text").IsRequired();
        builder.Property(x => x.Source)
            .HasConversion(s => s.ToString(), v => AnswerSource.From(v))
            .HasColumnType("varchar(32)")
            .IsRequired();
        builder.Property(x => x.CreatedBy).HasConversion(x => x.Value, x => new UserId(x));
        builder.Property(x => x.CreatedAt).HasConversion(new DateTimeUtcConverter());

        builder.HasIndex(x => new { x.AssessmentId, x.QuestionCode }).IsUnique();
        builder.HasIndex(x => x.AssessmentId);
    }
}