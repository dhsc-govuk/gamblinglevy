using Domain.Forms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Forms;

public class FormQuestionOptionConfiguration : IEntityTypeConfiguration<FormQuestionOption>
{
    public void Configure(EntityTypeBuilder<FormQuestionOption> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasConversion(x => x.Value, x => new FormQuestionOptionId(x));
        builder.Property(x => x.QuestionId).HasConversion(x => x.Value, x => new FormQuestionId(x));
        builder.Property(x => x.Value).HasColumnType("varchar(255)").IsRequired();
        builder.Property(x => x.Label).HasColumnType("varchar(500)").IsRequired();
        builder.Property(x => x.DisplayOrder).IsRequired();

        // The FK to FormQuestion (and its index) is configured on FormQuestionConfiguration.
    }
}