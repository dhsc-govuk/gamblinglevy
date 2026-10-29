using Domain.Collections;
using Domain.DataCollectionForms;
using Domain.Organisations;
using LanguageExt;

namespace Application.Common.Interfaces.Queries;

public interface ICollectionQueries
{
    // Organisation-wide load used by mutation handlers and the detail screen. By default excludes soft-deleted
    // records; pass includeDeleted to load a deleted record (e.g. for restore).
    Task<Option<Collection>> GetByIdAsync(CollectionId id, bool includeDeleted = false, CancellationToken cancellationToken = default);

    // All non-deleted collections — feeds the collections list screen, which groups by derived status client-side.
    Task<IReadOnlyList<Collection>> GetAllAsync(CancellationToken cancellationToken = default);

    // Soft-deleted collections — feeds the "Deleted collections" page.
    Task<IReadOnlyList<Collection>> GetDeletedAsync(CancellationToken cancellationToken = default);

    // Uniqueness guard for the name field. When updating, pass the current id to excludingId so a record does not
    // clash with itself.
    Task<bool> ExistsByNameAsync(string name, CollectionId? excludingId, CancellationToken cancellationToken = default);

    // Non-deleted collections the organisation is assigned to (collection_local_authorities). Status filtering
    // happens in memory at the call site — CollectionStatus is derived via GetStatus(), not a column.
    Task<IReadOnlyList<Collection>> GetAssignedToOrganisationAsync(OrganisationId organisationId, CancellationToken cancellationToken = default);

    // Published, non-deleted collections that are now Closed but have not had their DHSC closure notification
    // sent (BLUM-7719). Closure is derived, not stored, so this is a coarse SQL prefilter — the caller re-checks
    // GetStatus() in memory. maxAgeDays floors how far back a deadline may be: without it, a restore from backup
    // or a data reset that clears closure_notified_at would mail every admin once per historical window.
    // Loaded WITH tracking — the caller stamps MarkClosureNotified() and saves.
    Task<IReadOnlyList<Collection>> GetAwaitingClosureNotificationAsync(
        DateOnly today,
        int maxAgeDays,
        CancellationToken cancellationToken = default);

    // Published, non-deleted collections that closed (by deadline OR manual close) and are now owed, but have
    // not yet been sent, their LA "window closed" notification (BLUM-8155, timing per BLUM-8399). A separate
    // marker and a separate query from GetAwaitingClosureNotificationAsync above, because this can fire later
    // than that one and reaches a different audience (LA users of non-submitting organisations, not DHSC admins)
    // — the two must be able to notify independently.
    // Closure is derived, not stored, so this is a coarse SQL prefilter; the caller re-checks GetStatus() and
    // its own due date in memory. maxAgeDays floors how far back a close may be, same backstop reasoning as
    // GetAwaitingClosureNotificationAsync. Includes LocalAuthorities — read by
    // SubmissionAssignment.ResolveAssignedOrganisations on the branch where IsSubmittedByAllLas is not true.
    // AsNoTracking — the caller claims the notification atomically via
    // ICollectionRepository.TryClaimWindowClosedLaNotificationAsync rather than mutating this loaded aggregate.
    Task<IReadOnlyList<Collection>> GetAwaitingWindowClosedLaNotificationAsync(
        DateOnly today,
        int maxAgeDays,
        CancellationToken cancellationToken = default);

    // Published, non-deleted collections that are currently OPEN — everything CollectionReminderNotifier's sweep
    // has to consider. Openness is derived, not stored, so this is a coarse SQL prefilter: the caller re-checks
    // GetStatus() and then ReminderSchedule.DueOn() / WindowOpenedOn() in memory.
    //
    // Collections with NO configured reminders are returned deliberately. The BLUM-7721 window-open notification
    // is owed for every open collection whether or not an admin set a cadence up, and ReminderSchedule.DueOn
    // short-circuits on an empty reminder list — so the BLUM-7571 deadline pass is unaffected by their presence.
    //
    // Includes Reminders (the offsets the fire dates are computed from) AND LocalAuthorities (read by
    // SubmissionAssignment.ResolveAssignedOrganisations on the branch where IsSubmittedByAllLas is not true —
    // an un-included navigation is empty rather than an error, so omitting it silently reaches nobody). The rest
    // of WithGraph() is left out; nothing here reads Forms. AsNoTracking — unlike the closure sweep, nothing on
    // the Collection is mutated; the dispatch record is its own aggregate.
    Task<IReadOnlyList<Collection>> GetOpenForNotificationsAsync(
        DateOnly today,
        CancellationToken cancellationToken = default);

    // BLUM-9103: published collections whose window had not opened by `today` and that record a version of the form —
    // the ones a newly published version of it may move (Collection.FollowsLatestFormVersions). Soft-deleted ones are
    // included, so restoring one never brings back an older version. Status is derived, not stored, so this is a coarse
    // SQL prefilter: the caller re-checks FollowsLatestFormVersions. Loaded WITH tracking and with Forms only — the
    // caller moves the recorded versions and saves.
    Task<IReadOnlyList<Collection>> GetNotYetOpenUsingFormAsync(
        FormModuleId moduleId,
        DateOnly today,
        CancellationToken cancellationToken = default);

    // BLUM-9086: non-deleted collections that record a version of the form, newest first, for showing which collections
    // use each version. A draft is included when it records one (a revert to draft keeps the versions); a collection never
    // published records none, nor does a maturity catalogue section. Forms holds only the collection's row for this module
    // (a collection holds a module once), so it is a partial aggregate: never save it. AsNoTracking — read-only.
    Task<IReadOnlyList<Collection>> GetUsingFormVersionsAsync(
        FormModuleId moduleId,
        CancellationToken cancellationToken = default);

    // Non-deleted collections the organisation has at least one submission for, in a single read. Feeds the LA
    // submission-history union so a window the org submitted to but is no longer assigned to is included without an
    // N+1 per-collection lookup. AsNoTracking — read-only.
    Task<IReadOnlyList<Collection>> GetSubmittedByOrganisationAsync(OrganisationId organisationId, CancellationToken cancellationToken = default);
}