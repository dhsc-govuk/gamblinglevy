using Domain.Organisations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public class OrganisationsConfiguration : IEntityTypeConfiguration<Organisation>
{
    public void Configure(EntityTypeBuilder<Organisation> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasConversion(x => x.Value, x => new OrganisationId(x));
        builder.Property(x => x.Name).HasColumnType("varchar(255)").IsRequired();
        builder.Property(x => x.OnsCode).HasColumnType("varchar(64)");
        builder.Property(x => x.ECode).HasColumnType($"varchar({Organisation.ECodeMaxLength})").IsRequired();
        builder.Property(x => x.Region)
            .HasConversion(r => r.ToString(), v => Region.From(v))
            .HasColumnType("varchar(64)")
            .IsRequired();
        builder.Property(x => x.Type)
            .HasConversion(t => t.ToString(), v => OrgType.From(v))
            .HasColumnType("varchar(16)")
            .IsRequired();
        builder.Property(x => x.OnboardingStatus)
            .HasConversion(s => s.ToString(), v => OnboardingStatus.From(v))
            .HasColumnType("varchar(32)")
            .IsRequired();
        builder.Property(x => x.CreatedAt);
        builder.Property(x => x.UpdatedAt);

        builder.HasIndex(x => x.OnsCode)
            .IsUnique()
            .HasFilter("ons_code IS NOT NULL");
    }
}