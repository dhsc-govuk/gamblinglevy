using Domain.DataCollectionForms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.DataCollectionForms;

public class DcFormQuestionOptionConfiguration : IEntityTypeConfiguration<DcFormQuestionOption>
{
    public void Configure(EntityTypeBuilder<DcFormQuestionOption> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasConversion(x => x.Value, x => new DcFormQuestionOptionId(x));
        builder.Property(x => x.QuestionId).HasConversion(x => x.Value, x => new DcFormQuestionId(x));
        builder.Property(x => x.Value).HasColumnType("varchar(255)").IsRequired();
        builder.Property(x => x.Label).HasColumnType("varchar(500)").IsRequired();
        builder.Property(x => x.DisplayOrder).IsRequired();
        builder.Property(x => x.IsActive).IsRequired();

        // The FK to DcFormQuestion (and its index) is configured on DcFormQuestionConfiguration.
    }
}