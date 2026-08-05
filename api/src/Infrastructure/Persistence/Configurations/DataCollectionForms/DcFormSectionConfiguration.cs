using Domain.DataCollectionForms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.DataCollectionForms;

public class DcFormSectionConfiguration : IEntityTypeConfiguration<DcFormSection>
{
    public void Configure(EntityTypeBuilder<DcFormSection> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasConversion(x => x.Value, x => new DcFormSectionId(x));
        builder.Property(x => x.FormModuleId).HasConversion(x => x.Value, x => new FormModuleId(x));
        builder.Property(x => x.SectionNumber).IsRequired();
        builder.Property(x => x.Title).HasColumnType("varchar(255)").IsRequired();
        builder.Property(x => x.Description).HasColumnType("text");
        builder.Property(x => x.HelpText).HasColumnType("text");
        builder.Property(x => x.IsActive).IsRequired();

        builder.HasIndex(x => new { x.FormModuleId, x.SectionNumber });

        // The FK to FormModule (and its cascade) is configured on FormModuleConfiguration.
    }
}