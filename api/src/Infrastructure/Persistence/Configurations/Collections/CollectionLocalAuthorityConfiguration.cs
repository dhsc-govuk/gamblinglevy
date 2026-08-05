using Domain.Collections;
using Domain.Organisations;
using Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Collections;

public class CollectionLocalAuthorityConfiguration : IEntityTypeConfiguration<CollectionLocalAuthority>
{
    public void Configure(EntityTypeBuilder<CollectionLocalAuthority> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasConversion(x => x.Value, x => new CollectionLocalAuthorityId(x));
        builder.Property(x => x.CollectionId).HasConversion(x => x.Value, x => new CollectionId(x));
        builder.Property(x => x.LocalAuthorityId).HasConversion(x => x.Value, x => new OrganisationId(x));
        builder.Property(x => x.CreatedAt).HasConversion(new DateTimeUtcConverter());

        builder.HasIndex(x => new { x.CollectionId, x.LocalAuthorityId }).IsUnique();

        // The FK to Collection (and its cascade) is configured on CollectionConfiguration.
    }
}