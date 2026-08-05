using Domain.Categories;
using Domain.KeyResults;
using Domain.Users;
using Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.KeyResults;

public class KeyResultDefinitionConfiguration : IEntityTypeConfiguration<KeyResultDefinition>
{
    public void Configure(EntityTypeBuilder<KeyResultDefinition> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasConversion(x => x.Value, x => new KeyResultDefinitionId(x));

        // Id-only ActivityTaskId reference — reference data resolved by join, no navigation needed.
        builder.Property(x => x.ActivityTaskId).HasConversion(x => x.Value, x => new ActivityTaskId(x));
        builder.Property(x => x.Code).HasColumnType("varchar(100)").IsRequired();
        builder.Property(x => x.Label).HasColumnType("varchar(255)").IsRequired();
        builder.Property(x => x.DisplayOrder).IsRequired();
        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.CreatedBy).HasConversion(x => x.Value, x => new UserId(x));
        builder.Property(x => x.CreatedAt).HasConversion(new DateTimeUtcConverter());

        builder.HasIndex(x => new { x.ActivityTaskId, x.Code }).IsUnique();
    }
}