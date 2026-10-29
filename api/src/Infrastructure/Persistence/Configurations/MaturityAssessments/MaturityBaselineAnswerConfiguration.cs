using Domain.MaturityAssessments;
using Domain.Organisations;
using Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.MaturityAssessments;

public class MaturityBaselineAnswerConfiguration : IEntityTypeConfiguration<MaturityBaselineAnswer>
{
    public void Configure(EntityTypeBuilder<MaturityBaselineAnswer> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasConversion(x => x.Value, x => new MaturityBaselineAnswerId(x));
        builder.Property(x => x.OrganisationId).HasConversion(x => x.Value, x => new OrganisationId(x));
        builder.Property(x => x.QuestionCode).HasColumnType("varchar(100)").IsRequired();
        builder.Property(x => x.Value).HasColumnType("text").IsRequired();
        builder.Property(x => x.SurveyYear).IsRequired();
        builder.Property(x => x.ImportedAt).HasConversion(new DateTimeUtcConverter());

        // The seeder's idempotency backstop: two instances booting at once both pass the per-organisation guard,
        // and the loser fails here instead of duplicating.
        builder.HasIndex(x => new { x.OrganisationId, x.SurveyYear, x.QuestionCode }).IsUnique();
        builder.HasIndex(x => x.OrganisationId);
    }
}