using Domain.Collections;
using Domain.Organisations;
using Domain.Submissions;
using Domain.Users;
using Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Submissions;

public class SubmissionConfiguration : IEntityTypeConfiguration<Submission>
{
    public void Configure(EntityTypeBuilder<Submission> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasConversion(x => x.Value, x => new SubmissionId(x));
        builder.Property(x => x.CollectionId).HasConversion(x => x.Value, x => new CollectionId(x));
        builder.Property(x => x.OrganisationId).HasConversion(x => x.Value, x => new OrganisationId(x));
        builder.Property(x => x.ActivitiesConfirmedAt).HasConversion(new DateTimeUtcConverter());
        builder.Property(x => x.ActivitiesConfirmedBy)
            .HasConversion(x => x == null ? (Guid?)null : x.Value, x => x == null ? null : new UserId(x.Value));
        builder.Property(x => x.ActivitiesReviewRequired).IsRequired();
        builder.Property(x => x.SubmittedAt).HasConversion(new DateTimeUtcConverter());
        builder.Property(x => x.SubmittedBy)
            .HasConversion(x => x == null ? (Guid?)null : x.Value, x => x == null ? null : new UserId(x.Value));
        builder.Property(x => x.ReferenceNumber).HasMaxLength(32);
        builder.Property(x => x.ReportSnapshotJson).HasColumnType("jsonb");
        builder.Property(x => x.CreatedBy).HasConversion(x => x.Value, x => new UserId(x));
        builder.Property(x => x.CreatedAt).HasConversion(new DateTimeUtcConverter());
        builder.Property(x => x.UpdatedBy)
            .HasConversion(x => x == null ? (Guid?)null : x.Value, x => x == null ? null : new UserId(x.Value));
        builder.Property(x => x.UpdatedAt).HasConversion(new DateTimeUtcConverter());

        // One report per LA per collection.
        builder.HasIndex(x => new { x.CollectionId, x.OrganisationId }).IsUnique();

        // Reference numbers are unique across all submitted reports (Postgres treats NULLs as distinct, so
        // not-yet-submitted reports with a null reference are unaffected).
        builder.HasIndex(x => x.ReferenceNumber).IsUnique();

        builder.HasMany(x => x.Sections)
            .WithOne()
            .HasForeignKey(s => s.SubmissionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Metadata.FindNavigation(nameof(Submission.Sections))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}