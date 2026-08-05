using Domain.Activities;
using Domain.Users;
using Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public class ActivityExpectedResultConfiguration : IEntityTypeConfiguration<ActivityExpectedResult>
{
    public void Configure(EntityTypeBuilder<ActivityExpectedResult> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasConversion(x => x.Value, x => new ActivityExpectedResultId(x));

        // Id-only ActivityId reference (no FK / navigation) — mirrors ActivityAnswer so rows persist through
        // their own repository and are diff/merged on update (ids must stay stable — BLUM-6805).
        builder.Property(x => x.ActivityId).HasConversion(x => x.Value, x => new ActivityId(x));

        // Null = custom free-text row (the "Other" option / legacy rows); non-null references a KeyResultDefinition
        // code for the activity's type.
        builder.Property(x => x.KeyResultCode).HasColumnType("varchar(100)");
        builder.Property(x => x.Text).HasColumnType("text").IsRequired();
        builder.Property(x => x.DisplayOrder).IsRequired();
        builder.Property(x => x.CreatedBy).HasConversion(x => x.Value, x => new UserId(x));
        builder.Property(x => x.CreatedAt).HasConversion(new DateTimeUtcConverter());

        builder.HasIndex(x => x.ActivityId);
    }
}