using Domain.Common;

namespace Domain.Collections;

// A scheduled reminder for a collection, expressed as an offset (a number of days) either after the collection
// opens or before its submission deadline. Modelled as an id-only reference satellite so it persists through
// the Collection aggregate without EF tracking conflicts.
public class CollectionReminder : IEntity<CollectionReminderId>
{
    public CollectionReminderId Id { get; private set; }
    public CollectionId CollectionId { get; private set; }
    public ReminderOffsetType OffsetType { get; private set; }
    public int Days { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private CollectionReminder(
        CollectionReminderId id,
        CollectionId collectionId,
        ReminderOffsetType offsetType,
        int days,
        DateTime createdAt)
    {
        Id = id;
        CollectionId = collectionId;
        OffsetType = offsetType;
        Days = days;
        CreatedAt = createdAt;
    }

    public static CollectionReminder Create(
        CollectionReminderId id,
        CollectionId collectionId,
        ReminderOffsetType offsetType,
        int days)
    {
        Guard.NotNull(id, nameof(id));
        Guard.NotNull(collectionId, nameof(collectionId));
        Guard.NotNull(offsetType, nameof(offsetType));
        Guard.GreaterThanOrEqual(days, 0, nameof(days));

        return new CollectionReminder(id, collectionId, offsetType, days, DateTime.UtcNow);
    }
}