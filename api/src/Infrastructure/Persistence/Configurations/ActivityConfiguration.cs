using Domain.Activities;
using Domain.Categories;
using Domain.Organisations;
using Domain.Users;
using Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public class ActivityConfiguration : IEntityTypeConfiguration<Activity>
{
    public void Configure(EntityTypeBuilder<Activity> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasConversion(x => x.Value, x => new ActivityId(x));
        builder.Property(x => x.OrganisationId).HasConversion(x => x.Value, x => new OrganisationId(x));
        builder.Property(x => x.Title).HasColumnType("varchar(255)");
        builder.Property(x => x.Description).HasColumnType("text");
        builder.Property(x => x.CategoryId)
            .HasConversion(x => x == null ? (Guid?)null : x.Value, x => x == null ? null : new CategoryId(x.Value));
        builder.Property(x => x.TaskId)
            .HasConversion(x => x == null ? (Guid?)null : x.Value, x => x == null ? null : new ActivityTaskId(x.Value));
        builder.Property(x => x.CustomTaskText).HasColumnType("varchar(255)");
        builder.Property(x => x.Status)
            .HasConversion(s => s == null ? null : s.ToString(), v => v == null ? null : ActivityStatus.From(v))
            .HasColumnType("varchar(16)");
        builder.Property(x => x.IsComplete).IsRequired();
        builder.Property(x => x.StartMonth);
        builder.Property(x => x.StartYear);
        builder.Property(x => x.ClosureReason).HasColumnType("varchar(500)");
        builder.Property(x => x.IsFlaggedForReview).IsRequired();
        builder.Property(x => x.DeletedAt).HasConversion(new DateTimeUtcConverter());
        builder.Property(x => x.DeletedBy)
            .HasConversion(x => x == null ? (Guid?)null : x.Value, x => x == null ? null : new UserId(x.Value));
        builder.Property(x => x.CreatedBy).HasConversion(x => x.Value, x => new UserId(x));
        builder.Property(x => x.CreatedAt).HasConversion(new DateTimeUtcConverter());
        builder.Property(x => x.UpdatedAt).HasConversion(new DateTimeUtcConverter());

        builder.HasIndex(x => x.OrganisationId);
        builder.HasIndex(x => new { x.OrganisationId, x.Status });
        builder.HasIndex(x => new { x.OrganisationId, x.DeletedAt });
        builder.HasIndex(x => x.CategoryId);
        builder.HasIndex(x => x.TaskId);
    }
}