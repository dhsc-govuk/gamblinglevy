using Domain.Collections;
using Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations.Collections;

public class CollectionReminderDispatchConfiguration : IEntityTypeConfiguration<CollectionReminderDispatch>
{
    public void Configure(EntityTypeBuilder<CollectionReminderDispatch> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasConversion(x => x.Value, x => new CollectionReminderDispatchId(x));
        builder.Property(x => x.CollectionId).HasConversion(x => x.Value, x => new CollectionId(x));
        builder.Property(x => x.TriggerType)
            .HasConversion(s => s.ToString(), v => ReminderTrigger.From(v))
            .HasColumnType("varchar(32)")
            .IsRequired();
        builder.Property(x => x.ScheduledFor).HasConversion(new DateOnlyConverter()).IsRequired();
        builder.Property(x => x.OffsetType).HasColumnType("varchar(32)");
        builder.Property(x => x.Days);
        builder.Property(x => x.OutstandingOrganisationCount).IsRequired();
        builder.Property(x => x.ProviderEmailsSent).IsRequired();
        builder.Property(x => x.DhscEmailsSent).IsRequired();
        builder.Property(x => x.CreatedAt).HasConversion(new DateTimeUtcConverter());
        builder.Property(x => x.SentAt).HasConversion(new DateTimeUtcConverter());

        // The claim. Inserting this row BEFORE any email leaves is what stops two App Service instances — which
        // overlap during a deploy — both fanning the same reminder out to the same ~153 organisations: the loser
        // is refused here rather than after several hundred duplicate emails.
        //
        // TriggerType is part of the key so a later trigger kind landing on the same day as a scheduled reminder
        // does not collide with it (BLUM-7572's escalation; BLUM-7721's window-opened, which additionally carries
        // the once-per-collection constraint below).
        //
        // Manual sends are excluded from the constraint on purpose. The Reminder screen's shipped copy promises
        // "This will immediately send an email…", and an admin may legitimately press it twice in a day — after
        // adding a contact, say. A unique-violation 500 on the second press would be a regression against copy
        // that is already live.
        //
        // HasFilter takes RAW SQL, so the column name is written in snake_case by hand: EFCore.NamingConventions
        // rewrites property names, not the contents of this string.
        builder.HasIndex(x => new { x.CollectionId, x.TriggerType, x.ScheduledFor })
            .IsUnique()
            .HasFilter("trigger_type <> 'manual'");

        // BLUM-7721's claim is once per COLLECTION, not once per date — which is why it needs an index of its own
        // rather than riding on the one above.
        //
        // "The reporting window has opened" is a fact about a collection, announced once to every organisation
        // assigned to it. CollectionOpenDate is mutable while the window is open (UpdateDetails only refuses once
        // the collection is Closed, and UpdateCollectionCommand recomputes the date from the financial year and
        // quarter whenever WindowSameAsReportingPeriod is true) — so keying this claim on the date the way a
        // reminder is keyed would let an admin correcting the quarter re-announce the opening to every Local
        // Authority in the country. A reminder SHOULD re-fire on a moved date; an announcement must not.
        //
        // ScheduledFor still records the open date as it stood, for the history BLUM-7578 will read.
        //
        // The NAMED HasIndex overload is required, not cosmetic: an unnamed HasIndex is keyed by its property
        // set, so a second `HasIndex(x => x.CollectionId)` returns the SAME index builder as the plain lookup
        // index below and would silently reconfigure that one into a filtered unique constraint instead of
        // adding this.
        // HasDatabaseName is also required: EFCore.NamingConventions derives index names from their columns, so
        // without it this one is emitted as "..._collection_id1" — a name nobody reading a slow-query log or a
        // constraint violation could place.
        builder.HasIndex(x => x.CollectionId, "WindowOpenedClaim")
            .IsUnique()
            .HasDatabaseName("ix_collection_reminder_dispatches_window_opened")
            .HasFilter("trigger_type = 'window_opened'");

        builder.HasOne<Collection>()
            .WithMany()
            .HasForeignKey(x => x.CollectionId)
            .OnDelete(DeleteBehavior.Cascade);

        // Feeds the per-collection reminder history BLUM-7578 will read.
        builder.HasIndex(x => x.CollectionId);

        // FK + cascade declared from this side with NO navigation property on either end. Collection deliberately
        // does not gain a Dispatches collection: CollectionRepository.WithGraph() pulls every satellite into every
        // detail-screen read, and these rows accumulate for the life of a window. Deleting a collection still
        // takes its dispatch history with it.
    }
}