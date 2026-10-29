using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Domain.Collections;
using Domain.DataCollectionForms;
using Domain.Organisations;
using LanguageExt;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class CollectionRepository(ApplicationDbContext dbContext)
    : RepositoryBase<Collection, CollectionId>(dbContext), ICollectionRepository, ICollectionQueries
{
    Task ICollectionRepository.UpdateAsync(Collection collection, CancellationToken cancellationToken)
        => UpdateAsync(collection, cancellationToken);

    public Task LockForFormChangesAsync(IReadOnlyCollection<CollectionId> collectionIds, CancellationToken cancellationToken = default)
        => CollectionRowLocks.ForFormChangesAsync(Context, collectionIds, cancellationToken);

    public Task LockForReportStartAsync(CollectionId collectionId, CancellationToken cancellationToken = default)
        => CollectionRowLocks.ForReportStartAsync(Context, collectionId, cancellationToken);

    public async Task<Option<Collection>> GetByIdAsync(
        CollectionId id,
        bool includeDeleted = false,
        CancellationToken cancellationToken = default)
    {
        // Tracked load: command handlers mutate the aggregate (incl. replacing owned Forms/LocalAuthorities/
        // Reminders) and rely on the change tracker to diff added/removed children on save.
        var query = WithGraph(Context.Collections)
            .Where(x => x.Id == id);

        if (!includeDeleted)
        {
            query = query.Where(x => x.DeletedAt == null);
        }

        var entity = await query.FirstOrDefaultAsync(cancellationToken);
        return entity is null ? Option<Collection>.None : Option<Collection>.Some(entity);
    }

    public async Task<IReadOnlyList<Collection>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await WithGraph(Context.Collections.AsNoTracking())
            .Where(x => x.DeletedAt == null)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Collection>> GetDeletedAsync(CancellationToken cancellationToken = default)
    {
        return await WithGraph(Context.Collections.AsNoTracking())
            .Where(x => x.DeletedAt != null)
            .OrderByDescending(x => x.DeletedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Collection>> GetAwaitingClosureNotificationAsync(
        DateOnly today,
        int maxAgeDays,
        CancellationToken cancellationToken = default)
    {
        // Tracked, and deliberately WITHOUT WithGraph(): the caller only stamps ClosureNotifiedAt, so pulling the
        // forms/LA/reminder satellites for every closed window would be waste.
        //
        // The predicate mirrors GetStatus()'s Closed arm (IsManuallyClosed || today > SubmissionDeadline) as far
        // as SQL can, but it is only a prefilter — CollectionStatus is derived, so the caller has the last word.
        var earliestDeadline = today.AddDays(-Math.Max(0, maxAgeDays));

        return await Context.Collections
            .Where(x => x.DeletedAt == null
                && !x.IsDraft
                && x.ClosureNotifiedAt == null
                && x.SubmissionDeadline != null
                && (x.IsManuallyClosed || x.SubmissionDeadline < today)
                && x.SubmissionDeadline >= earliestDeadline)
            .OrderBy(x => x.SubmissionDeadline)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Collection>> GetAwaitingWindowClosedLaNotificationAsync(
        DateOnly today,
        int maxAgeDays,
        CancellationToken cancellationToken = default)
    {
        // AsNoTracking — unlike GetAwaitingClosureNotificationAsync, the caller no longer mutates
        // WindowClosedLaNotifiedAt through this aggregate; it claims the notification atomically via
        // TryClaimWindowClosedLaNotificationAsync instead (a send-then-stamp ordering here let two racing sweep
        // instances both decide to send, unlike the small, tolerable DHSC closure-notice duplicate). Only
        // LocalAuthorities is included — that's the one satellite SubmissionAssignment.ResolveAssignedOrganisations
        // reads. Forms and Reminders are left out, same reasoning as GetAwaitingClosureNotificationAsync.
        //
        // "1 day after the window closed" (BLUM-8399) — CollectionWindowClosedLaNotifier.GetNotificationDueDate is
        // the authority and re-checks in memory; this is only a coarse prefilter, expressed as an OR across both
        // paths since that calculation can't be translated into SQL.
        //
        // Deadline path: the deadline is the last day LAs can submit, so the window closes at the end of it and the
        // email is owed from deadline + 1, i.e. SubmissionDeadline <= today - 1.
        // Manual-close path: owed from the day after the close, i.e. any close BEFORE the start of today. Compared
        // with < midnight today, not <= midnight yesterday — the latter excluded every close made after 00:00
        // yesterday, so a manual close also waited two days.
        var earliestDeadline = today.AddDays(-Math.Max(0, maxAgeDays));
        var deadlineCutoff = today.AddDays(-1);
        var manualCloseCutoff = DateTime.SpecifyKind(today.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var earliestManualClose = DateTime.SpecifyKind(earliestDeadline.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

        return await Context.Collections
            .AsNoTracking()
            .Include(x => x.LocalAuthorities)
            .Where(x => x.DeletedAt == null
                && !x.IsDraft
                && x.WindowClosedLaNotifiedAt == null
                && ((x.SubmissionDeadline != null && x.SubmissionDeadline <= deadlineCutoff && x.SubmissionDeadline >= earliestDeadline)
                    || (x.ManuallyClosedAt != null && x.ManuallyClosedAt < manualCloseCutoff && x.ManuallyClosedAt >= earliestManualClose)))
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> TryClaimWindowClosedLaNotificationAsync(
        CollectionId collectionId,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var updated = await Context.Collections
            .Where(x => x.Id == collectionId && x.WindowClosedLaNotifiedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.WindowClosedLaNotifiedAt, utcNow), cancellationToken);

        return updated == 1;
    }

    public async Task ReleaseWindowClosedLaNotificationClaimAsync(
        CollectionId collectionId,
        CancellationToken cancellationToken = default)
    {
        await Context.Collections
            .Where(x => x.Id == collectionId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.WindowClosedLaNotifiedAt, (DateTime?)null), cancellationToken);
    }

    public async Task<IReadOnlyList<Collection>> GetOpenForNotificationsAsync(
        DateOnly today,
        CancellationToken cancellationToken = default)
    {
        // AsNoTracking, because the caller mutates nothing on the Collection — the dispatch record is its own
        // aggregate. But BOTH satellites are needed and neither is optional:
        //
        //   * Reminders carries the offsets the fire dates are computed from;
        //   * LocalAuthorities is read by SubmissionAssignment.ResolveAssignedOrganisations on the branch where
        //     IsSubmittedByAllLas is NOT true. Omitting it does not fail — the navigation is simply empty, so an
        //     explicitly-assigned window resolves to nobody, reminds nobody, reports an outstanding count of 0
        //     and still consumes its trigger point. Silent, and invisible to any test that only covers all-LAs
        //     windows. ShouldEmailTheAssignedLas_WhenTheWindowNamesThemExplicitly pins it.
        //
        // Forms is still left out — nothing here reads it.
        //
        // There is deliberately NO `Reminders.Any()` arm. It was here for BLUM-7571, whose only trigger came from
        // a configured reminder; BLUM-7721's window-open notification is owed for every open collection, cadence
        // or not, so filtering them out here would silently skip exactly the collections an admin never
        // configured. ReminderSchedule.DueOn short-circuits on an empty reminder list, so the deadline pass sees
        // no change beyond a few more rows to walk.
        //
        // The predicate mirrors GetStatus()'s Open arm as far as SQL can, but it is only a prefilter —
        // CollectionStatus is derived, so the caller re-checks it. Note the open-date arm allows NULL: GetStatus()
        // treats a missing open date as Open (today < null is false) and ReminderSchedule.DueOn handles it
        // explicitly, so excluding those rows here would be stricter than the status logic this claims to mirror.
        // Publishing has required an open date since BLUM-6625, so this is a guard for legacy rows rather than a
        // live path — but a prefilter that silently disagrees with GetStatus() is the wrong thing to leave behind.
        return await Context.Collections
            .AsNoTracking()
            .Include(x => x.Reminders)
            .Include(x => x.LocalAuthorities)
            .Where(x => x.DeletedAt == null
                && !x.IsDraft
                && !x.IsManuallyClosed
                && x.SubmissionDeadline != null
                && x.SubmissionDeadline >= today
                && (x.CollectionOpenDate == null || x.CollectionOpenDate <= today))
            .OrderBy(x => x.SubmissionDeadline)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Collection>> GetNotYetOpenUsingFormAsync(
        FormModuleId moduleId,
        DateOnly today,
        CancellationToken cancellationToken = default)
    {
        // Tracked, and with Forms only: the caller moves recorded versions and saves through UpdateAsync. No DeletedAt
        // filter, on purpose (see ICollectionQueries). The predicate mirrors GetStatus()'s Scheduled arm as far as SQL
        // can; the caller has the last word.
        return await Context.Collections
            .Include(x => x.Forms)
            .Where(x => !x.IsDraft
                && !x.IsManuallyClosed
                && x.CollectionOpenDate != null
                && x.CollectionOpenDate > today
                && x.Forms.Any(f => f.FormModuleId == moduleId && f.FormModuleVersionId != null))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Collection>> GetUsingFormVersionsAsync(
        FormModuleId moduleId,
        CancellationToken cancellationToken = default)
    {
        // Filtered include: each collection comes back holding only its row for this module.
        return await Context.Collections
            .AsNoTracking()
            .Include(x => x.Forms.Where(f => f.FormModuleId == moduleId))
            .Where(x => x.DeletedAt == null
                && x.Forms.Any(f => f.FormModuleId == moduleId && f.FormModuleVersionId != null))
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> ExistsByNameAsync(
        string name,
        CollectionId? excludingId,
        CancellationToken cancellationToken = default)
    {
        var normalised = (name ?? string.Empty).Trim().ToLower();

        var query = Context.Collections
            .AsNoTracking()
            .Where(x => x.DeletedAt == null && x.Name.ToLower() == normalised);

        if (excludingId is not null)
        {
            // Compare against a non-nullable CollectionId so EF can translate the value-object inequality
            // (a nullable CollectionId? operand is not translatable).
            CollectionId excluded = excludingId;
            query = query.Where(x => x.Id != excluded);
        }

        return await query.AnyAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Collection>> GetAssignedToOrganisationAsync(
        OrganisationId organisationId,
        CancellationToken cancellationToken = default)
    {
        // Coarse prefilter for the LA-facing surface: non-deleted collections this org could reach, either by an
        // explicit assignment row or by the "submitted by all LAs" flag (persisted with NO
        // collection_local_authorities rows). Callers MUST still apply SubmissionEligibility.IsAssignedTo, the
        // authoritative type-aware gate — all-LAs only counts for Local-Authority orgs, which can't be checked here
        // without translating an OrgType value object into SQL. IsSubmittedByAllLas is nullable; "== true" excludes null/false drafts.
        return await WithGraph(Context.Collections.AsNoTracking())
            .Where(x => x.DeletedAt == null
                && (x.IsSubmittedByAllLas == true
                    || x.LocalAuthorities.Any(la => la.LocalAuthorityId == organisationId)))
            .OrderBy(x => x.SubmissionDeadline)
            .ThenBy(x => x.CollectionOpenDate)
            .ThenBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Collection>> GetSubmittedByOrganisationAsync(
        OrganisationId organisationId,
        CancellationToken cancellationToken = default)
    {
        // Non-deleted collections the org has any submission for — quarterly report OR maturity assessment
        // (BLUM-7859) — in one EXISTS query: no Contains over a list of value-object ids (which would not
        // translate) and no N+1. Scalar equalities only.
        return await WithGraph(Context.Collections.AsNoTracking())
            .Where(c => c.DeletedAt == null
                && (Context.Submissions.Any(s => s.CollectionId == c.Id && s.OrganisationId == organisationId)
                    || Context.MaturityAssessments.Any(m => m.CollectionId == c.Id && m.OrganisationId == organisationId)))
            .ToListAsync(cancellationToken);
    }

    private static IQueryable<Collection> WithGraph(IQueryable<Collection> query)
        => query
            .Include(c => c.Forms)
            .Include(c => c.LocalAuthorities)
            .Include(c => c.Reminders);
}