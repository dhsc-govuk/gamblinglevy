using Application.Common.Interfaces.Repositories;
using Domain.Collections;
using Domain.Organisations;
using Domain.Submissions;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class SubmissionSpendEntryRepository(ApplicationDbContext dbContext)
    : RepositoryBase<SubmissionSpendEntry, SubmissionSpendEntryId>(dbContext), ISubmissionSpendEntryRepository
{
    public async Task<IReadOnlyList<SubmissionSpendEntry>> GetForSubmissionAsync(
        SubmissionId submissionId,
        CancellationToken cancellationToken = default)
    {
        return await Context.SubmissionSpendEntries
            .AsNoTracking()
            .Where(e => e.SubmissionId == submissionId)
            .ToListAsync(cancellationToken);
    }

    // Replaces just one kind for the submission. The kind is filtered in memory — comparing the SpendEntryKind
    // value object in SQL does not translate (the Status value object is handled the same way elsewhere), and the
    // per-submission row count is tiny.
    //
    // Read, remove, add and save are four steps, so two saves of one submission (a double-click on Save, a client
    // retry) could both read "nothing to remove" and both insert, doubling the LA's reported spend. The unique
    // index does not catch it: an Actual row's quarter is always NULL and Postgres treats NULLs as distinct. So
    // the replace runs in one transaction that first locks the parent submission row. The second save waits on
    // that line until the first commits, and its read of the existing rows — which must stay below the lock —
    // then sees them. NO KEY UPDATE is the strength a plain UPDATE takes: it conflicts with itself but not with
    // the key-share lock an insert into a child table takes on the submission.
    //
    // The offline upload calls this inside ITransactionRunner, and Npgsql does not nest transactions, so an open
    // one is joined rather than a second begun; the lock is then held until the upload commits.
    public async Task ReplaceForSubmissionAsync(
        SubmissionId submissionId,
        SpendEntryKind kind,
        IReadOnlyList<SubmissionSpendEntry> entries,
        CancellationToken cancellationToken = default)
    {
        await using var ownTransaction = Context.Database.CurrentTransaction is null
            ? await Context.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            await Context.Database.ExecuteSqlAsync(
                $"SELECT 1 FROM submissions WHERE id = {submissionId.Value} FOR NO KEY UPDATE",
                cancellationToken);

            var existing = await Context.SubmissionSpendEntries
                .Where(e => e.SubmissionId == submissionId)
                .ToListAsync(cancellationToken);

            var toRemove = existing.Where(e => e.Kind.Equals(kind)).ToList();
            if (toRemove.Count > 0)
            {
                Context.SubmissionSpendEntries.RemoveRange(toRemove);
            }

            if (entries.Count > 0)
            {
                await Context.SubmissionSpendEntries.AddRangeAsync(entries, cancellationToken);
            }

            await Context.SaveChangesAsync(cancellationToken);

            if (ownTransaction is not null)
            {
                await ownTransaction.CommitAsync(cancellationToken);
            }
        }
        catch when (ownTransaction is not null)
        {
            // Disposing the uncommitted transaction rolls the rows back, but the context would go on tracking
            // them as saved, and a later SaveChanges in the same request would work from what the rollback undid
            // (the same reason TransactionRunner clears it). A joined transaction is left to its owner.
            Context.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<decimal> SumActualForSubmissionsAsync(
        IReadOnlyList<SubmissionId> submissionIds,
        CancellationToken cancellationToken = default)
    {
        var total = 0m;
        foreach (var submissionId in submissionIds)
        {
            var rows = await Context.SubmissionSpendEntries
                .AsNoTracking()
                .Where(e => e.SubmissionId == submissionId)
                .ToListAsync(cancellationToken);
            total += rows.Where(e => e.Kind.IsActual).Sum(e => e.Amount);
        }

        return total;
    }

    // One query across every year the organisation has reported in, rather than a fetch per earlier submission
    // (BLUM-7703). The Actual/Forecast split is done in memory for the reason given above — SpendEntryKind does not
    // translate to SQL — so the entity is materialised alongside its collection's financial year; the org's lifetime
    // row count here is a few hundred at most. collection.DeletedAt == null mirrors the submission queries: a
    // mistaken collection that was later soft-deleted must not contribute spend.
    public async Task<IReadOnlyList<OrganisationActivitySpendRow>> GetActualActivitySpendByFinancialYearAsync(
        OrganisationId organisationId,
        CancellationToken cancellationToken = default)
    {
        var rows = await (
            from entry in Context.SubmissionSpendEntries.AsNoTracking()
            join submission in Context.Submissions.AsNoTracking() on entry.SubmissionId equals submission.Id
            join collection in Context.Collections.AsNoTracking() on submission.CollectionId equals collection.Id
            where submission.OrganisationId == organisationId
                && collection.DeletedAt == null
                && entry.ActivityId != null
            select new { Entry = entry, collection.FinancialYear })
            .ToListAsync(cancellationToken);

        return rows
            .Where(r => r.Entry.Kind.IsActual)
            .Select(r => new OrganisationActivitySpendRow(r.Entry.ActivityId!.Value, r.FinancialYear, r.Entry.Amount))
            .ToList();
    }

    public async Task<IReadOnlyDictionary<Guid, decimal>> SumActualByOrganisationForCollectionAsync(
        CollectionId collectionId,
        CancellationToken cancellationToken = default)
    {
        // Strongly-typed-id equality (CollectionId / SubmissionId) translates to SQL; Contains over a list of
        // value-object ids does not, so load the window's submissions then sum Actual spend per submission — the same
        // proven shape as SumActualForSubmissionsAsync. Row counts here are tiny (≤ ~160 orgs, a handful of entries).
        // Only SUBMITTED (finalised) submissions count: Actual spend is written while a report is still a draft, so an
        // unsubmitted org's draft spend must not leak into the DHSC export's "total reported spend". SubmittedAt != null
        // translates to SQL (the computed IsSubmitted property does not).
        var submissions = await Context.Submissions
            .AsNoTracking()
            .Where(s => s.CollectionId == collectionId && s.SubmittedAt != null)
            .ToListAsync(cancellationToken);

        var totals = new Dictionary<Guid, decimal>();
        foreach (var submission in submissions)
        {
            var rows = await Context.SubmissionSpendEntries
                .AsNoTracking()
                .Where(e => e.SubmissionId == submission.Id)
                .ToListAsync(cancellationToken);
            totals[submission.OrganisationId.Value] = rows.Where(e => e.Kind.IsActual).Sum(e => e.Amount);
        }

        return totals;
    }
}