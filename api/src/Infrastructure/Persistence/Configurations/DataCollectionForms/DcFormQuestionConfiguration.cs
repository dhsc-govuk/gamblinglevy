using Domain.DataCollectionForms;
using Domain.Forms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.DataCollectionForms;

public class DcFormQuestionConfiguration : IEntityTypeConfiguration<DcFormQuestion>
{
    public void Configure(EntityTypeBuilder<DcFormQuestion> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasConversion(x => x.Value, x => new DcFormQuestionId(x));
        builder.Property(x => x.FormModuleId).HasConversion(x => x.Value, x => new FormModuleId(x));
        builder.Property(x => x.SectionId)
            .HasConversion(x => x == null ? (Guid?)null : x.Value, x => x == null ? null : new DcFormSectionId(x.Value));
        builder.Property(x => x.Code).HasColumnType("varchar(100)").IsRequired();
        builder.Property(x => x.Label).HasColumnType("varchar(500)").IsRequired();
        builder.Property(x => x.QuestionType)
            .HasConversion(t => t.ToString(), v => QuestionType.From(v))
            .HasColumnType("varchar(32)")
            .IsRequired();
        builder.Property(x => x.DisplayOrder).IsRequired();
        builder.Property(x => x.IsRequired).IsRequired();
        builder.Property(x => x.Hint).HasColumnType("text");
        builder.Property(x => x.HelpText).HasColumnType("text");
        builder.Property(x => x.ErrorMessage).HasColumnType("text");
        builder.Property(x => x.MaxLengthErrorMessage).HasColumnType("text");
        builder.Property(x => x.IsActive).IsRequired();

        builder.HasIndex(x => new { x.FormModuleId, x.SectionId });

        // Options are owned by the question and replaced wholesale on update; cascade-delete with the question.
        builder.HasMany(x => x.Options)
            .WithOne()
            .HasForeignKey(o => o.QuestionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Metadata.FindNavigation(nameof(DcFormQuestion.Options))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        // The FK to FormModule (and its cascade) is configured on FormModuleConfiguration.
    }
}