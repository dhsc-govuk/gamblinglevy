using Domain.Collections;

namespace Application.Common.Interfaces.Repositories;

// Writes to collection_reminder_dispatches (BLUM-7571).
//
// AddAsync is a CLAIM, not a log entry: the unique index on (collection_id, trigger_type, scheduled_for) for
// non-manual triggers is what stops two App Service instances — which overlap during a deploy — both fanning a
// reminder out to the same ~153 organisations. The caller inserts BEFORE sending and treats a DbUpdateException
// as "somebody else has this one".
public interface ICollectionReminderDispatchRepository
{
    Task<CollectionReminderDispatch> AddAsync(CollectionReminderDispatch dispatch, CancellationToken cancellationToken = default);

    Task UpdateAsync(CollectionReminderDispatch dispatch, CancellationToken cancellationToken = default);

    // Releases a claim. Used only when every send was refused because Notify is not configured for this purpose —
    // nothing was delivered, so nothing is owed as "already sent".
    Task RemoveAsync(CollectionReminderDispatch dispatch, CancellationToken cancellationToken = default);

    // Clears the change tracker after a failed claim. A failed SaveChanges leaves the entity tracked on the
    // scoped DbContext, and every later write in the same sweep then fails with it.
    void ForgetTrackedChanges();
}