using Domain.Collections;
using Domain.DataCollectionForms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Collections;

public class CollectionFormConfiguration : IEntityTypeConfiguration<CollectionForm>
{
    public void Configure(EntityTypeBuilder<CollectionForm> builder)
    {
        // CollectionForm has no surrogate key — its Id is a get-only alias of CollectionId. Use the composite
        // (CollectionId, FormModuleId) as the primary key and ignore the alias so EF doesn't try to map it.
        builder.Ignore(x => x.Id);
        builder.HasKey(x => new { x.CollectionId, x.FormModuleId });

        builder.Property(x => x.CollectionId).HasConversion(x => x.Value, x => new CollectionId(x));
        builder.Property(x => x.FormModuleId).HasConversion(x => x.Value, x => new FormModuleId(x));

        // BLUM-9103: the version the collection uses. Restrict, like the versions' own FK: a version is never deleted.
        builder.Property(x => x.FormModuleVersionId)
            .HasConversion(x => x == null ? (Guid?)null : x.Value, x => x == null ? null : new FormModuleVersionId(x.Value));
        builder.HasOne<FormModuleVersion>()
            .WithMany()
            .HasForeignKey(x => x.FormModuleVersionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.IsVersionChosen).HasDefaultValue(false).IsRequired();

        // The FK to Collection (and its cascade) is configured on CollectionConfiguration.
    }
}