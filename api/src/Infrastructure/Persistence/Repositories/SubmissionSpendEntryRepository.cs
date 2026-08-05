using Application.Common.Interfaces.Repositories;
using Domain.Collections;
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
    public async Task ReplaceForSubmissionAsync(
        SubmissionId submissionId,
        SpendEntryKind kind,
        IReadOnlyList<SubmissionSpendEntry> entries,
        CancellationToken cancellationToken = default)
    {
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