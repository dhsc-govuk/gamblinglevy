using Domain.Collections;
using Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Collections;

public class CollectionReminderConfiguration : IEntityTypeConfiguration<CollectionReminder>
{
    public void Configure(EntityTypeBuilder<CollectionReminder> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasConversion(x => x.Value, x => new CollectionReminderId(x));
        builder.Property(x => x.CollectionId).HasConversion(x => x.Value, x => new CollectionId(x));
        builder.Property(x => x.OffsetType)
            .HasConversion(s => s.ToString(), v => ReminderOffsetType.From(v))
            .HasColumnType("varchar(32)")
            .IsRequired();
        builder.Property(x => x.Days).IsRequired();
        builder.Property(x => x.CreatedAt).HasConversion(new DateTimeUtcConverter());

        builder.HasIndex(x => x.CollectionId);

        // The FK to Collection (and its cascade) is configured on CollectionConfiguration.
    }
}