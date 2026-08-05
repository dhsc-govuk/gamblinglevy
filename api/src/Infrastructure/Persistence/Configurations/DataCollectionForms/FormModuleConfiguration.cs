using Domain.DataCollectionForms;
using Domain.Users;
using Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.DataCollectionForms;

public class FormModuleConfiguration : IEntityTypeConfiguration<FormModule>
{
    public void Configure(EntityTypeBuilder<FormModule> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasConversion(x => x.Value, x => new FormModuleId(x));
        builder.Property(x => x.Code).HasColumnType("varchar(100)").IsRequired();
        builder.Property(x => x.Name).HasColumnType("varchar(255)").IsRequired();
        builder.Property(x => x.Description).HasColumnType("text");
        builder.Property(x => x.DisplayOrder).IsRequired();
        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.Status)
            .HasConversion(s => s.ToString(), v => FormStatus.From(v))
            .HasColumnType("varchar(32)")
            .IsRequired();
        builder.Property(x => x.PublishedAt).HasConversion(new DateTimeUtcConverter());
        builder.Property(x => x.PublishedBy)
            .HasConversion(x => x == null ? (Guid?)null : x.Value, x => x == null ? null : new UserId(x.Value));
        builder.Property(x => x.CreatedBy).HasConversion(x => x.Value, x => new UserId(x));
        builder.Property(x => x.CreatedAt).HasConversion(new DateTimeUtcConverter());
        builder.Property(x => x.UpdatedBy)
            .HasConversion(x => x == null ? (Guid?)null : x.Value, x => x == null ? null : new UserId(x.Value));
        builder.Property(x => x.UpdatedAt).HasConversion(new DateTimeUtcConverter());

        builder.HasIndex(x => x.Code).IsUnique();

        // Sections and questions are owned by the module and cascade-delete with it.
        builder.HasMany(x => x.Sections)
            .WithOne()
            .HasForeignKey(s => s.FormModuleId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Metadata.FindNavigation(nameof(FormModule.Sections))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(x => x.Questions)
            .WithOne()
            .HasForeignKey(q => q.FormModuleId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Metadata.FindNavigation(nameof(FormModule.Questions))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}