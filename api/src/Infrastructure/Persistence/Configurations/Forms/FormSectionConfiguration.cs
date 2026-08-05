using Domain.Categories;
using Domain.Forms;
using Domain.Users;
using Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Forms;

public class FormSectionConfiguration : IEntityTypeConfiguration<FormSection>
{
    public void Configure(EntityTypeBuilder<FormSection> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasConversion(x => x.Value, x => new FormSectionId(x));
        builder.Property(x => x.CategoryId)
            .HasConversion(x => x == null ? (Guid?)null : x.Value, x => x == null ? null : new CategoryId(x.Value));
        builder.Property(x => x.ActivityTaskId)
            .HasConversion(x => x == null ? (Guid?)null : x.Value, x => x == null ? null : new ActivityTaskId(x.Value));
        builder.Property(x => x.Title).HasColumnType("varchar(255)").IsRequired();
        builder.Property(x => x.Description).HasColumnType("text");
        builder.Property(x => x.DisplayOrder).IsRequired();
        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.CreatedBy).HasConversion(x => x.Value, x => new UserId(x));
        builder.Property(x => x.CreatedAt).HasConversion(new DateTimeUtcConverter());
        builder.Property(x => x.UpdatedAt).HasConversion(new DateTimeUtcConverter());

        builder.HasIndex(x => x.DisplayOrder);
    }
}