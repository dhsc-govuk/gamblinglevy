using Domain.Categories;
using Domain.Users;
using Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public class ActivityTaskConfiguration : IEntityTypeConfiguration<ActivityTask>
{
    public void Configure(EntityTypeBuilder<ActivityTask> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasConversion(x => x.Value, x => new ActivityTaskId(x));
        builder.Property(x => x.CategoryId).HasConversion(x => x.Value, x => new CategoryId(x));
        builder.Property(x => x.TaskName).HasColumnType("varchar(255)").IsRequired();
        builder.Property(x => x.Description).HasColumnType("text");
        builder.Property(x => x.SequenceOrder).IsRequired();
        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.CreatedBy).HasConversion(x => x.Value, x => new UserId(x));
        builder.Property(x => x.CreatedAt).HasConversion(new DateTimeUtcConverter());
        builder.Property(x => x.UpdatedAt).HasConversion(new DateTimeUtcConverter());

        builder.HasIndex(x => x.CategoryId);
    }
}