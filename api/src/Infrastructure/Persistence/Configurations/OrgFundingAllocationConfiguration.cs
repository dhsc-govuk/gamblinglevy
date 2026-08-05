using Domain.Funding;
using Domain.Organisations;
using Domain.Users;
using Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public class OrgFundingAllocationConfiguration : IEntityTypeConfiguration<OrgFundingAllocation>
{
    public void Configure(EntityTypeBuilder<OrgFundingAllocation> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasConversion(x => x.Value, x => new OrgFundingAllocationId(x));
        builder.Property(x => x.OrganisationId).HasConversion(x => x.Value, x => new OrganisationId(x));
        builder.Property(x => x.FundingWindowId).HasConversion(x => x.Value, x => new FundingWindowId(x));
        builder.Property(x => x.AllocatedAmount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(x => x.CreatedAt).HasConversion(new DateTimeUtcConverter());
        builder.Property(x => x.CreatedBy).HasConversion(x => x.Value, x => new UserId(x));
        builder.Property(x => x.AmendedAt).HasConversion(new DateTimeUtcConverter());
        builder.Property(x => x.AmendedBy)
            .HasConversion(x => x == null ? (Guid?)null : x.Value, x => x == null ? null : new UserId(x.Value));

        builder.HasIndex(x => new { x.OrganisationId, x.FundingWindowId }).IsUnique();
        builder.HasIndex(x => x.OrganisationId);
    }
}