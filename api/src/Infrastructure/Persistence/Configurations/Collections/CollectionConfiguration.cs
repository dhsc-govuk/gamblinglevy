using Domain.Collections;
using Domain.Users;
using Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Collections;

public class CollectionConfiguration : IEntityTypeConfiguration<Collection>
{
    public void Configure(EntityTypeBuilder<Collection> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasConversion(x => x.Value, x => new CollectionId(x));
        builder.Property(x => x.Name).HasColumnType("varchar(255)").IsRequired();
        builder.Property(x => x.Purpose).HasColumnType("text");
        // Setup questions are nullable: a draft may have any of them unanswered (null), and null must round-trip
        // as null — EF skips the conversions for null values, so the converters stay non-null-aware.
        builder.Property(x => x.Type)
            .HasConversion(s => s!.ToString(), v => CollectionType.From(v))
            .HasColumnType("varchar(32)");
        builder.Property(x => x.FinancialYear).HasColumnType("varchar(16)");
        builder.Property(x => x.Quarter);
        builder.Property(x => x.ReportingPeriodStart).HasConversion(new DateOnlyConverter());
        builder.Property(x => x.ReportingPeriodEnd).HasConversion(new DateOnlyConverter());
        builder.Property(x => x.WindowSameAsReportingPeriod);
        builder.Property(x => x.CollectionOpenDate).HasConversion(new DateOnlyConverter());
        builder.Property(x => x.SubmissionDeadline).HasConversion(new DateOnlyConverter());
        builder.Property(x => x.IsSubmittedByAllLas);
        builder.Property(x => x.IsDraft).IsRequired();
        builder.Property(x => x.IsManuallyClosed).IsRequired();
        builder.Property(x => x.ManuallyClosedAt).HasConversion(new DateTimeUtcConverter());
        builder.Property(x => x.ClosureNotifiedAt).HasConversion(new DateTimeUtcConverter());
        builder.Property(x => x.WindowClosedLaNotifiedAt).HasConversion(new DateTimeUtcConverter());
        builder.Property(x => x.CreatedBy).HasConversion(x => x.Value, x => new UserId(x));
        builder.Property(x => x.CreatedAt).HasConversion(new DateTimeUtcConverter());
        builder.Property(x => x.UpdatedBy)
            .HasConversion(x => x == null ? (Guid?)null : x.Value, x => x == null ? null : new UserId(x.Value));
        builder.Property(x => x.UpdatedAt).HasConversion(new DateTimeUtcConverter());
        builder.Property(x => x.DeletedAt).HasConversion(new DateTimeUtcConverter());
        builder.Property(x => x.DeletedBy)
            .HasConversion(x => x == null ? (Guid?)null : x.Value, x => x == null ? null : new UserId(x.Value));

        // Status is DERIVED via GetStatus() from the draft/manually-closed flags and the window dates — it is not
        // a persisted property, so no Status column is mapped.

        // Unique among live collections only, matching ExistsByNameAsync: a soft-deleted collection must not block
        // re-using its name (BLUM-9093). HasFilter is raw SQL, so the column is snake_case by hand.
        builder.HasIndex(x => x.Name).IsUnique().HasFilter("deleted_at IS NULL");
        builder.HasIndex(x => x.DeletedAt);

        builder.HasMany(x => x.Forms)
            .WithOne()
            .HasForeignKey(f => f.CollectionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Metadata.FindNavigation(nameof(Collection.Forms))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(x => x.LocalAuthorities)
            .WithOne()
            .HasForeignKey(la => la.CollectionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Metadata.FindNavigation(nameof(Collection.LocalAuthorities))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(x => x.Reminders)
            .WithOne()
            .HasForeignKey(r => r.CollectionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Metadata.FindNavigation(nameof(Collection.Reminders))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}