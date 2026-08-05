using Domain.Categories;
using Domain.KeyResults;
using Domain.Users;
using Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.KeyResults;

public class KeyResultQuestionConfiguration : IEntityTypeConfiguration<KeyResultQuestion>
{
    public void Configure(EntityTypeBuilder<KeyResultQuestion> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasConversion(x => x.Value, x => new KeyResultQuestionId(x));

        builder.Property(x => x.ActivityTaskId).HasConversion(x => x.Value, x => new ActivityTaskId(x));
        builder.Property(x => x.KeyResultCode).HasColumnType("varchar(100)").IsRequired();
        builder.Property(x => x.Code).HasColumnType("varchar(100)").IsRequired();
        builder.Property(x => x.StatusScope)
            .HasConversion(s => s.ToString(), v => QuestionStatusScope.From(v))
            .HasColumnType("varchar(32)")
            .IsRequired();
        builder.Property(x => x.Label).HasColumnType("text").IsRequired();
        builder.Property(x => x.Hint).HasColumnType("text");
        builder.Property(x => x.FieldType)
            .HasConversion(t => t.ToString(), v => ProtocolFieldType.From(v))
            .HasColumnType("varchar(32)")
            .IsRequired();
        builder.Property(x => x.IsRequiredOnComplete).IsRequired();
        builder.Property(x => x.MinValue).HasColumnType("numeric");
        builder.Property(x => x.MaxValue).HasColumnType("numeric");

        // Options are a small owned value collection persisted as jsonb (repo convention; Npgsql dynamic JSON is
        // enabled in ConfigurePersistenceServices).
        builder.Property(x => x.Options).HasColumnType("jsonb");

        builder.Property(x => x.PrefillSource).HasColumnType("varchar(64)");
        builder.Property(x => x.ConditionalQuestionCode).HasColumnType("varchar(100)");
        builder.Property(x => x.ConditionalValue).HasColumnType("varchar(255)");
        builder.Property(x => x.ErrorMessage).HasColumnType("text");
        builder.Property(x => x.DisplayOrder).IsRequired();
        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.CreatedBy).HasConversion(x => x.Value, x => new UserId(x));
        builder.Property(x => x.CreatedAt).HasConversion(new DateTimeUtcConverter());

        // One row per phrasing variant: the Active and Closed/Finished variants share a Code and differ by scope.
        builder.HasIndex(x => new { x.ActivityTaskId, x.Code, x.StatusScope }).IsUnique();
        builder.HasIndex(x => x.ActivityTaskId);
    }
}