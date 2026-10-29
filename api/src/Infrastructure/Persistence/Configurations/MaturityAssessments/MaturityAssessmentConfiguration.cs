using Domain.Collections;
using Domain.MaturityAssessments;
using Domain.Organisations;
using Domain.Users;
using Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.MaturityAssessments;

public class MaturityAssessmentConfiguration : IEntityTypeConfiguration<MaturityAssessment>
{
    public void Configure(EntityTypeBuilder<MaturityAssessment> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasConversion(x => x.Value, x => new MaturityAssessmentId(x));
        builder.Property(x => x.CollectionId).HasConversion(x => x.Value, x => new CollectionId(x));
        builder.Property(x => x.OrganisationId).HasConversion(x => x.Value, x => new OrganisationId(x));
        builder.Property(x => x.SubmittedAt).HasConversion(new DateTimeUtcConverter());
        builder.Property(x => x.SubmittedBy)
            .HasConversion(x => x == null ? (Guid?)null : x.Value, x => x == null ? null : new UserId(x.Value));
        builder.Property(x => x.ReferenceNumber).HasMaxLength(32);
        builder.Property(x => x.SnapshotJson).HasColumnType("jsonb");
        builder.Property(x => x.CreatedBy).HasConversion(x => x.Value, x => new UserId(x));
        builder.Property(x => x.CreatedAt).HasConversion(new DateTimeUtcConverter());
        builder.Property(x => x.UpdatedBy)
            .HasConversion(x => x == null ? (Guid?)null : x.Value, x => x == null ? null : new UserId(x.Value));
        builder.Property(x => x.UpdatedAt).HasConversion(new DateTimeUtcConverter());

        // One assessment per LA per collection.
        builder.HasIndex(x => new { x.CollectionId, x.OrganisationId }).IsUnique();

        // Reference numbers are unique across all submitted assessments (Postgres treats NULLs as distinct, so
        // not-yet-submitted assessments are unaffected). The constraint name must contain "reference_number" —
        // MaturityAssessmentRepository keys its retryable-conflict detection on it.
        builder.HasIndex(x => x.ReferenceNumber).IsUnique();

        builder.HasMany(x => x.Sections)
            .WithOne()
            .HasForeignKey(s => s.AssessmentId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Metadata.FindNavigation(nameof(MaturityAssessment.Sections))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}