using Domain.Funding;
using Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public class FundingWindowConfiguration : IEntityTypeConfiguration<FundingWindow>
{
    public void Configure(EntityTypeBuilder<FundingWindow> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasConversion(x => x.Value, x => new FundingWindowId(x));
        builder.Property(x => x.FinancialYear).HasColumnType("varchar(16)").IsRequired();
        builder.Property(x => x.WindowLabel).HasColumnType("varchar(64)").IsRequired();
        builder.Property(x => x.CreatedAt).HasConversion(new DateTimeUtcConverter());

        builder.HasIndex(x => x.FinancialYear).IsUnique();
    }
}