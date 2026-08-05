using Domain.Locations;
using Domain.Organisations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public class LocationsConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasConversion(x => x.Value, x => new LocationId(x));
        builder.Property(x => x.OrganisationId).HasConversion(x => x.Value, x => new OrganisationId(x));
        builder.Property(x => x.Name).HasColumnType("varchar(255)");
        builder.Property(x => x.IsActive);
        builder.HasOne<Organisation>(x => x.Organisation)
            .WithMany()
            .HasForeignKey(x => x.OrganisationId)
            .HasConstraintName("fk_locations_organisations_organisation_id")
            .OnDelete(DeleteBehavior.Restrict);
    }
}