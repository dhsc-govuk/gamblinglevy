using Domain.Activities;
using Domain.Forms;
using Domain.Users;
using Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public class ActivityAnswerConfiguration : IEntityTypeConfiguration<ActivityAnswer>
{
    public void Configure(EntityTypeBuilder<ActivityAnswer> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasConversion(x => x.Value, x => new ActivityAnswerId(x));

        // Id-only references (no FK / navigation), so answers persist through their own repository without
        // tracking conflicts and survive a referenced question being deleted.
        builder.Property(x => x.ActivityId).HasConversion(x => x.Value, x => new ActivityId(x));
        builder.Property(x => x.QuestionId).HasConversion(x => x.Value, x => new FormQuestionId(x));
        builder.Property(x => x.QuestionCode).HasColumnType("varchar(100)").IsRequired();
        builder.Property(x => x.Value).HasColumnType("text").IsRequired();
        builder.Property(x => x.CreatedBy).HasConversion(x => x.Value, x => new UserId(x));
        builder.Property(x => x.CreatedAt).HasConversion(new DateTimeUtcConverter());
        builder.Property(x => x.UpdatedAt).HasConversion(new DateTimeUtcConverter());

        builder.HasIndex(x => x.ActivityId);
        builder.HasIndex(x => new { x.ActivityId, x.QuestionCode }).IsUnique();
    }
}