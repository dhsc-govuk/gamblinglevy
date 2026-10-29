using Domain.Activities;
using Domain.Submissions;
using Domain.Users;
using Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Submissions;

public class SubmissionActivityAnswerConfiguration : IEntityTypeConfiguration<SubmissionActivityAnswer>
{
    public void Configure(EntityTypeBuilder<SubmissionActivityAnswer> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasConversion(x => x.Value, x => new SubmissionActivityAnswerId(x));

        // Id-only references — answers are replaced wholesale per activity through their own repository.
        builder.Property(x => x.SubmissionId).HasConversion(x => x.Value, x => new SubmissionId(x));
        builder.Property(x => x.ActivityId).HasConversion(x => x.Value, x => new ActivityId(x));
        builder.Property(x => x.QuestionCode).HasColumnType("varchar(100)").IsRequired();
        builder.Property(x => x.Value).HasColumnType("text").IsRequired();
        builder.Property(x => x.CreatedBy).HasConversion(x => x.Value, x => new UserId(x));
        builder.Property(x => x.CreatedAt).HasConversion(new DateTimeUtcConverter());

        builder.HasIndex(x => new { x.SubmissionId, x.ActivityId, x.QuestionCode }).IsUnique();
        builder.HasIndex(x => new { x.SubmissionId, x.ActivityId });

        // The carry-forward lookup (GetPriorSubmittedActivityAnswersAsync) filters on ActivityId alone and joins
        // out to submissions/collections from there — the pair above leads with SubmissionId, so neither serves it.
        builder.HasIndex(x => x.ActivityId);
    }
}