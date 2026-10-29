using Domain.DataCollectionForms;
using Domain.Users;
using Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.DataCollectionForms;

public class FormModuleVersionConfiguration : IEntityTypeConfiguration<FormModuleVersion>
{
    public void Configure(EntityTypeBuilder<FormModuleVersion> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasConversion(x => x.Value, x => new FormModuleVersionId(x));
        builder.Property(x => x.FormModuleId).HasConversion(x => x.Value, x => new FormModuleId(x)).IsRequired();
        builder.Property(x => x.VersionNumber).IsRequired();
        builder.Property(x => x.Definition).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.ContentHash).HasColumnType("varchar(64)").IsRequired();
        builder.Property(x => x.PublishedAt).HasConversion(new DateTimeUtcConverter()).IsRequired();

        // No FK to users, like FormModule.PublishedBy.
        builder.Property(x => x.PublishedBy).HasConversion(x => x.Value, x => new UserId(x)).IsRequired();

        // The number a concurrent publish races for: the loser's insert fails here and its publish rolls back.
        builder.HasIndex(x => new { x.FormModuleId, x.VersionNumber }).IsUnique();

        // Restrict, never cascade: deleting a module must not take its published history with it (modules are
        // archived, not deleted, so this only ever refuses).
        builder.HasOne<FormModule>()
            .WithMany()
            .HasForeignKey(x => x.FormModuleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}