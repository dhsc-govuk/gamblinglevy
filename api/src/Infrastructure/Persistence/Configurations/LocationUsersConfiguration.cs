using Domain.Locations;
using Domain.LocationUsers;
using Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public class LocationUsersConfiguration : IEntityTypeConfiguration<LocationUser>
{
    public void Configure(EntityTypeBuilder<LocationUser> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasConversion(x => x.Value, x => new LocationUserId(x));
        builder.Property(x => x.UserId).HasConversion(x => x.Value, x => new UserId(x));
        builder.Property(x => x.LocationId).HasConversion(x => x.Value, x => new LocationId(x));
        builder.HasOne<User>(x => x.User)
            .WithOne()
            .HasForeignKey<LocationUser>(x => x.UserId)
            .HasConstraintName("fk_location_users_users_user_id")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Location>(x => x.Location)
            .WithMany()
            .HasForeignKey(x => x.LocationId)
            .HasConstraintName("fk_location_users_locations_location_id")
            .OnDelete(DeleteBehavior.Restrict);
    }
}