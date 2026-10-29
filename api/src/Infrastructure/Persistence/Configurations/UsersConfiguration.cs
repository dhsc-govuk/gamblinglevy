using Domain.Users;
using Domain.ValueObjects;
using Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;
public class UsersConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasConversion(x => x.Value, x => new UserId(x));
        builder.Property(x => x.Name).HasColumnType("jsonb");
        builder.Property(x => x.Email).HasColumnType("varchar(320)");
        builder.Property(x => x.SubId).HasConversion(x => x.Value, x => new SubId(x));
        builder.Property(x => x.Status)
            .HasColumnType("varchar(32)")
            .HasConversion(x => x.ToString(), x => UserStatus.From(x));
        builder.Property(x => x.Role)
            .HasColumnType("varchar(255)")
            .HasConversion(x => x.ToString(), x => UserRole.From(x));

        builder.Property(x => x.CreatedAt)
            .HasConversion(new DateTimeUtcConverter())
            .HasDefaultValueSql("timezone('utc', now())");

        builder.Property(x => x.UpdatedAt)
            .HasConversion(new DateTimeUtcConverter())
            .IsRequired(false);

        builder.Property(x => x.InvitationHeldAt)
            .HasConversion(new DateTimeUtcConverter())
            .IsRequired(false);
    }
}