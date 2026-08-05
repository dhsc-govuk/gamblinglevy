using Domain.Organisations;
using Domain.Users;
using Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public class OrgNeighbourConfiguration : IEntityTypeConfiguration<OrgNeighbour>
{
    public void Configure(EntityTypeBuilder<OrgNeighbour> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasConversion(x => x.Value, x => new OrgNeighbourId(x));
        builder.Property(x => x.OrganisationId).HasConversion(x => x.Value, x => new OrganisationId(x));
        builder.Property(x => x.NeighbourOrgId).HasConversion(x => x.Value, x => new OrganisationId(x));
        builder.Property(x => x.Source)
            .HasConversion(s => s.ToString(), v => NeighbourSource.From(v))
            .HasColumnType("varchar(32)")
            .IsRequired();
        builder.Property(x => x.ReferenceTool).HasColumnType("varchar(128)");
        builder.Property(x => x.ImportedAt).HasConversion(new DateTimeUtcConverter());
        builder.Property(x => x.ImportedBy).HasConversion(x => x.Value, x => new UserId(x));

        builder.HasIndex(x => x.OrganisationId);
    }
}