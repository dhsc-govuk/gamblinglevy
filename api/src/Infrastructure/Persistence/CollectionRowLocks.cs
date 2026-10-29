using Domain.Collections;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

// BLUM-9084: the row lock that keeps "have reports started in this collection?" true until the decision built on it
// commits. A change to a collection's forms or recorded versions is refused, or skipped, once a report has started,
// and that check used to run with nothing stopping a report starting straight after it, so a report could begin on
// the questions the change was about to replace.
//
// - Whoever changes a collection's forms or versions on that check (UpdateCollectionCommand, PublishFormModuleCommand,
//   FormModuleVersionSync) takes ForFormChangesAsync first, then checks.
// - The one place a report starts (CreateSubmissionCommand) takes ForReportStartAsync first, then reads the collection.
//
// FOR SHARE conflicts with FOR NO KEY UPDATE but not with itself, so a report start and a change wait for each other
// and two report starts never do. Whichever locks first wins: a change that waited sees the report and is refused, and
// a report start that waited reads the forms the change left. NO KEY UPDATE is the strength the collection's own
// UPDATE takes, and it still lets other work insert rows that reference the collection (a key-share lock), so that
// work is not held up.
// Several rows are locked in one statement, in id order, so two publishes moving overlapping collections cannot
// deadlock.
//
// Both hold the lock until the transaction commits, so both refuse to run outside one: a lock taken in autocommit is
// released by the statement that takes it. CollectionRepository exposes them to handlers; FormModuleVersionSync runs in
// the boot scope, which cannot hold a repository, and calls them directly.
public static class CollectionRowLocks
{
    public static async Task ForFormChangesAsync(
        ApplicationDbContext dbContext,
        IReadOnlyCollection<CollectionId> collectionIds,
        CancellationToken cancellationToken)
    {
        RequireTransaction(dbContext);
        if (collectionIds.Count == 0)
        {
            return;
        }

        var ids = collectionIds.Select(id => id.Value).Distinct().ToArray();
        await dbContext.Database.ExecuteSqlAsync(
            $"SELECT 1 FROM collections WHERE id = ANY({ids}) ORDER BY id FOR NO KEY UPDATE",
            cancellationToken);
    }

    public static async Task ForReportStartAsync(
        ApplicationDbContext dbContext,
        CollectionId collectionId,
        CancellationToken cancellationToken)
    {
        RequireTransaction(dbContext);
        await dbContext.Database.ExecuteSqlAsync(
            $"SELECT 1 FROM collections WHERE id = {collectionId.Value} FOR SHARE",
            cancellationToken);
    }

    private static void RequireTransaction(ApplicationDbContext dbContext)
    {
        if (dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("A collection row lock must be taken inside a transaction, or it is released at once.");
        }
    }
}