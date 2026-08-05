using Domain.Submissions;
using Domain.Users;
using Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Submissions;

public class SubmissionFeedbackAnswerConfiguration : IEntityTypeConfiguration<SubmissionFeedbackAnswer>
{
    public void Configure(EntityTypeBuilder<SubmissionFeedbackAnswer> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasConversion(x => x.Value, x => new SubmissionFeedbackAnswerId(x));

        // Id-only references — answers are replaced wholesale per submission through their own repository.
        builder.Property(x => x.SubmissionId).HasConversion(x => x.Value, x => new SubmissionId(x));
        builder.Property(x => x.QuestionCode).HasColumnType("varchar(100)").IsRequired();
        builder.Property(x => x.Value).HasColumnType("text").IsRequired();
        builder.Property(x => x.CreatedBy).HasConversion(x => x.Value, x => new UserId(x));
        builder.Property(x => x.CreatedAt).HasConversion(new DateTimeUtcConverter());

        builder.HasIndex(x => new { x.SubmissionId, x.QuestionCode }).IsUnique();
        builder.HasIndex(x => x.SubmissionId);
    }
}