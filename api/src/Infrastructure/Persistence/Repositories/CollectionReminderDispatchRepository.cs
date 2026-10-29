using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Domain.Collections;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class CollectionReminderDispatchRepository(ApplicationDbContext dbContext)
    : RepositoryBase<CollectionReminderDispatch, CollectionReminderDispatchId>(dbContext),
        ICollectionReminderDispatchRepository,
        ICollectionReminderDispatchQueries
{
    Task ICollectionReminderDispatchRepository.UpdateAsync(
        CollectionReminderDispatch dispatch,
        CancellationToken cancellationToken)
        => UpdateAsync(dispatch, cancellationToken);

    Task ICollectionReminderDispatchRepository.RemoveAsync(
        CollectionReminderDispatch dispatch,
        CancellationToken cancellationToken)
        => RemoveAsync(dispatch, cancellationToken);

    // A failed SaveChanges leaves the entity tracked on the scoped DbContext, so every later write in the same
    // sweep fails with it — the defect recorded against the bulk LA import (BLUM-7050), where one rejected row
    // took every subsequent row in the file down with it. The notifier calls this after a losing claim.
    public void ForgetTrackedChanges() => Context.ChangeTracker.Clear();

    public async Task<IReadOnlyList<ClaimedReminderDispatch>> GetClaimedScheduledAsync(
        IReadOnlyCollection<CollectionId> collectionIds,
        DateOnly notBefore,
        CancellationToken cancellationToken = default)
    {
        if (collectionIds.Count == 0)
        {
            return [];
        }

        // Materialised as the strongly-typed ids rather than their Guid values: CollectionId maps through
        // HasConversion, so Contains over the id records translates to the same IN (...) on collection_id.
        var ids = collectionIds.Distinct().ToList();

        var rows = await Context.CollectionReminderDispatches
            .AsNoTracking()
            .Where(x => ids.Contains(x.CollectionId)
                && x.TriggerType == ReminderTrigger.Scheduled
                && x.ScheduledFor >= notBefore)
            .Select(x => new { x.CollectionId, x.ScheduledFor })
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => new ClaimedReminderDispatch(row.CollectionId.Value, row.ScheduledFor))
            .ToList();
    }

    public async Task<IReadOnlyList<Guid>> GetCollectionsWithWindowOpenedClaimAsync(
        IReadOnlyCollection<CollectionId> collectionIds,
        CancellationToken cancellationToken = default)
    {
        if (collectionIds.Count == 0)
        {
            return [];
        }

        var ids = collectionIds.Distinct().ToList();

        // TriggerType is compared, never projected. The comparison translates to `trigger_type = 'window_opened'`
        // in SQL, whereas selecting the column would run it back through HasConversion and therefore through
        // ReminderTrigger.From — which THROWS on a value this build does not know. One row written by a newer
        // deployment (BLUM-7572's 'escalation', say, during a slot swap) would then take the whole sweep down,
        // silently, because CollectionReminderNotificationHostedService swallows everything.
        var rows = await Context.CollectionReminderDispatches
            .AsNoTracking()
            .Where(x => ids.Contains(x.CollectionId) && x.TriggerType == ReminderTrigger.WindowOpened)
            .Select(x => x.CollectionId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return rows.Select(id => id.Value).ToList();
    }
}