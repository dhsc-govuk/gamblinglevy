using Domain.Forms;
using Domain.Users;
using Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Forms;

public class FormQuestionConfiguration : IEntityTypeConfiguration<FormQuestion>
{
    public void Configure(EntityTypeBuilder<FormQuestion> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasConversion(x => x.Value, x => new FormQuestionId(x));
        builder.Property(x => x.SectionId).HasConversion(x => x.Value, x => new FormSectionId(x));
        builder.Property(x => x.Code).HasColumnType("varchar(100)").IsRequired();
        builder.Property(x => x.Label).HasColumnType("varchar(500)").IsRequired();
        builder.Property(x => x.Hint).HasColumnType("text");
        builder.Property(x => x.HelpText).HasColumnType("text");
        builder.Property(x => x.HelpTextSummary).HasColumnType("varchar(500)");
        builder.Property(x => x.QuestionType)
            .HasConversion(t => t.ToString(), v => QuestionType.From(v))
            .HasColumnType("varchar(16)")
            .IsRequired();
        builder.Property(x => x.IsRequired).IsRequired();
        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.DisplayOrder).IsRequired();
        builder.Property(x => x.ConditionalQuestionCode).HasColumnType("varchar(100)");
        builder.Property(x => x.ConditionalValue).HasColumnType("varchar(255)");
        builder.Property(x => x.CreatedBy).HasConversion(x => x.Value, x => new UserId(x));
        builder.Property(x => x.CreatedAt).HasConversion(new DateTimeUtcConverter());
        builder.Property(x => x.UpdatedAt).HasConversion(new DateTimeUtcConverter());

        // Options are owned by the question and replaced wholesale on update; cascade-delete with the question.
        builder.HasMany(x => x.Options)
            .WithOne()
            .HasForeignKey(o => o.QuestionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Metadata.FindNavigation(nameof(FormQuestion.Options))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        // Code is the unique answer key + conditional reference across the whole form.
        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasIndex(x => new { x.SectionId, x.DisplayOrder });
    }
}