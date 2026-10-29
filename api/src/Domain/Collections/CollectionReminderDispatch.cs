using Domain.Common;

namespace Domain.Collections;

// The record that a reminder for a collection went out on a given day (BLUM-7571).
//
// A separate aggregate rather than another satellite of Collection, for two reasons. Collection.SetReminders
// CLEARS the reminder list and re-creates every row with a fresh CollectionReminderId on every save, so a
// "sent" stamp hung off CollectionReminder is silently wiped by any edit to the collection — including one that
// changed only its name. And these rows accumulate for the life of a window, whereas CollectionRepository's
// WithGraph() pulls every satellite into every detail-screen read.
//
// The identity that matters is (CollectionId, TriggerType, ScheduledFor): ScheduledFor is the RESOLVED calendar
// date a reminder was due, so moving the deadline turns "before_deadline 7" into a different date — a genuinely
// new trigger point that should be sent — while re-saving the collection unchanged does not.
public class CollectionReminderDispatch : IEntity<CollectionReminderDispatchId>
{
    public CollectionReminderDispatchId Id { get; private set; }
    public CollectionId CollectionId { get; private set; }
    public ReminderTrigger TriggerType { get; private set; }

    // The day the reminder was due. For a manual send this is simply the day the admin pressed the button.
    public DateOnly ScheduledFor { get; private set; }

    // Which configured reminder resolved to ScheduledFor. Provenance only — null for a manual send, and never
    // used to decide whether a dispatch has happened (ScheduledFor is).
    public string? OffsetType { get; private set; }
    public int? Days { get; private set; }

    public int OutstandingOrganisationCount { get; private set; }
    public int ProviderEmailsSent { get; private set; }
    public int DhscEmailsSent { get; private set; }

    public DateTime CreatedAt { get; private set; }

    // Null until the fan-out finishes. A row created but never stamped means an instance claimed this trigger
    // point and then died (or was unloaded) part-way through mailing — the state BLUM-7578's delivery log will
    // want to surface. The deploy backfill also leaves it null on purpose: those rows suppress a send, they do
    // not record one.
    public DateTime? SentAt { get; private set; }

    private CollectionReminderDispatch(
        CollectionReminderDispatchId id,
        CollectionId collectionId,
        ReminderTrigger triggerType,
        DateOnly scheduledFor,
        string? offsetType,
        int? days,
        DateTime createdAt)
    {
        Id = id;
        CollectionId = collectionId;
        TriggerType = triggerType;
        ScheduledFor = scheduledFor;
        OffsetType = offsetType;
        Days = days;
        CreatedAt = createdAt;
    }

    public static CollectionReminderDispatch Create(
        CollectionReminderDispatchId id,
        CollectionId collectionId,
        ReminderTrigger triggerType,
        DateOnly scheduledFor,
        DateTime utcNow,
        string? offsetType = null,
        int? days = null)
    {
        Guard.NotNull(id, nameof(id));
        Guard.NotNull(collectionId, nameof(collectionId));
        Guard.NotNull(triggerType, nameof(triggerType));

        return new CollectionReminderDispatch(id, collectionId, triggerType, scheduledFor, offsetType, days, utcNow);
    }

    // Records what the fan-out actually achieved. Takes no actor and does NOT set any UpdatedBy/UpdatedAt — the
    // sweep has nobody behind it, and this is bookkeeping rather than user-facing state. Same reasoning as
    // Collection.MarkClosureNotified().
    public void MarkSent(int outstandingOrganisationCount, int providerEmailsSent, int dhscEmailsSent, DateTime utcNow)
    {
        OutstandingOrganisationCount = outstandingOrganisationCount;
        ProviderEmailsSent = providerEmailsSent;
        DhscEmailsSent = dhscEmailsSent;
        SentAt = utcNow;
    }
}