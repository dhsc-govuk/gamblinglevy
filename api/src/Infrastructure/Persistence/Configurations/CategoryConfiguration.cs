using Domain.Categories;
using Domain.Users;
using Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasConversion(x => x.Value, x => new CategoryId(x));
        builder.Property(x => x.Name).HasColumnType("varchar(255)").IsRequired();
        builder.Property(x => x.Description).HasColumnType("text");
        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.SequenceOrder).IsRequired();
        builder.Property(x => x.CreatedBy).HasConversion(x => x.Value, x => new UserId(x));
        builder.Property(x => x.CreatedAt).HasConversion(new DateTimeUtcConverter());
        builder.Property(x => x.UpdatedAt).HasConversion(new DateTimeUtcConverter());

        builder.HasIndex(x => x.IsActive);

        // The name is the identity everything outside the seeders uses: SeedFormDesigner resolves its sections by
        // it, CategoryNames pins two of them, and the client mirrors those. It is also what makes the row-scoped
        // guard on SeedJointPartnershipTaxonomyAsync safe — two instances booting together can both pass an
        // AnyAsync check, and without this index the loser would silently insert a duplicate category rather than
        // fail, leaving two identical options in the wizard and a section seeded for only one of them (BLUM-7860).
        builder.HasIndex(x => x.Name).IsUnique();
    }
}