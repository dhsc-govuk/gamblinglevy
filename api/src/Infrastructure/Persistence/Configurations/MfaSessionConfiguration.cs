using Domain.Users;
using Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public class MfaSessionConfiguration : IEntityTypeConfiguration<MfaSession>
{
    public void Configure(EntityTypeBuilder<MfaSession> builder)
    {
        builder.ToTable("mfa_sessions");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasConversion(x => x.Value, x => new MfaSessionId(x));

        builder.Property(x => x.UserId)
            .HasConversion(x => x.Value, x => new UserId(x));

        builder.HasIndex(x => x.UserId);
        builder.HasIndex(x => x.ExpiresAt);

        builder.Property(x => x.IpAddressHash)
            .HasColumnType("varchar(64)")
            .IsRequired();

        builder.Property(x => x.UserAgentHash)
            .HasColumnType("varchar(64)")
            .IsRequired();

        // Display-only metadata for the user's own sessions list (BLUM-7241). Sized for a full IPv6 literal
        // and for the longest label DeviceLabelResolver can compose; both are always written, so sessions
        // created before the columns existed carry an empty string and render as "Not recorded".
        builder.Property(x => x.IpAddress)
            .HasColumnType("varchar(45)")
            .IsRequired();

        builder.Property(x => x.DeviceLabel)
            .HasColumnType("varchar(100)")
            .IsRequired();

        // BLUM-7848. Nullable, so existing rows need no backfill: null reads as "still on the sign-in address".
        builder.Property(x => x.LastSeenIpAddress)
            .HasColumnType("varchar(45)");

        builder.Property(x => x.LastSeenIpAddressHash)
            .HasColumnType("varchar(64)");

        builder.Property(x => x.ExpiresAt)
            .HasConversion(new DateTimeUtcConverter())
            .IsRequired();

        builder.Property(x => x.IsRevoked)
            .HasDefaultValue(false);

        builder.Property(x => x.CreatedAt)
            .HasConversion(new DateTimeUtcConverter())
            .HasDefaultValueSql("timezone('utc', now())")
            .IsRequired();

        // Stored as text rather than an ordinal: this column is read by hand during incident investigation,
        // and a bare 0/1 there would mean nothing.
        builder.Property(x => x.EndedReason)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(x => x.LastActivityAt)
            .HasConversion(new DateTimeUtcConverter())
            .HasDefaultValueSql("timezone('utc', now())")
            .IsRequired();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}