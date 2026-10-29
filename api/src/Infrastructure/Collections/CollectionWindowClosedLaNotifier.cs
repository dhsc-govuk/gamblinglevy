using Application.Common.Interfaces;
using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.Common.Settings;
using Application.Submissions;
using Domain.Collections;
using Domain.Organisations;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Collections;

public class CollectionWindowClosedLaNotifier(
    ICollectionQueries collectionQueries,
    ICollectionRepository collectionRepository,
    IOrganisationQueries organisationQueries,
    IOrganisationUserRepository organisationUserRepository,
    OutstandingSubmissionResolver outstandingSubmissionResolver,
    ICollectionWindowClosedLaEmailService emailService,
    IDateTimeProvider dateTimeProvider,
    ApplicationSettings settings,
    ILogger<CollectionWindowClosedLaNotifier> logger)
    : ICollectionWindowClosedLaNotifier
{
    public async Task<int> SweepAsync(CancellationToken cancellationToken = default)
    {
        // Same clock GetStatus() reads, so the SQL prefilter and the in-memory re-check agree.
        var today = DateOnly.FromDateTime(dateTimeProvider.GetUtcNow());
        var maxAgeDays = ResolveMaxAgeDays();

        var candidates = await collectionQueries.GetAwaitingWindowClosedLaNotificationAsync(today, maxAgeDays, cancellationToken);
        if (candidates.Count == 0)
        {
            return 0;
        }

        // Read once per sweep, not once per collection: the organisation estate cannot change between two
        // collections in the same pass, and on a "submitted by all LAs" window this is the whole ~153-organisation
        // estate.
        var allOrganisations = await organisationQueries.GetOrganisations(cancellationToken);

        var notified = 0;
        foreach (var collection in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await NotifyIfClosedAsync(collection, allOrganisations, cancellationToken))
            {
                notified++;
            }
        }

        if (notified > 0)
        {
            logger.LogInformation("Sent the LA window-closed notification for {Count} data collection(s).", notified);
        }

        return notified;
    }

    private async Task<bool> NotifyIfClosedAsync(
        Collection collection,
        IReadOnlyList<Organisation> allOrganisations,
        CancellationToken cancellationToken)
    {
        var utcNow = dateTimeProvider.GetUtcNow();

        if (collection.WindowClosedLaNotifiedAt is not null)
        {
            return false;
        }

        // GetStatus() is the authority on what "closed" means; the query predicate is only a prefilter.
        if (!collection.GetStatus(utcNow).Equals(CollectionStatus.Closed))
        {
            return false;
        }

        if (collection.SubmissionDeadline is not { } deadline)
        {
            // The query predicate already requires SubmissionDeadline OR ManuallyClosedAt to be set, so this is
            // unreachable for a manual close; a natural (deadline) close cannot reach here without one either.
            // Restated for the compiler, same as CollectionReminderNotifier.DispatchAsync — and the email's
            // "Deadline" personalisation field genuinely needs a value, so this is a hard refusal either way.
            return false;
        }

        // "1 day after the window closed" (BLUM-8399). The SQL prefilter already applied this, but it is only a
        // prefilter; the sweep is the authority, same convention as GetStatus() above.
        var today = DateOnly.FromDateTime(utcNow);
        var dueOn = GetNotificationDueDate(collection);
        if (dueOn is null || today < dueOn.Value)
        {
            return false;
        }

        // Claim BEFORE resolving or sending anything. App Service overlaps instances during a deploy, and a
        // send-then-stamp ordering would let two racing sweeps — or one sweep resuming after a restart mid-loop —
        // both decide this collection is still owed a send and fan out to every outstanding contact twice. This
        // is the deliberate inversion of CollectionClosureNotifier's send-then-stamp, which is right there because
        // a duplicate reaches a handful of DHSC admins and wrong here because it can reach every contact at every
        // outstanding LA — the same reasoning CollectionReminderNotifier's dispatch claim is built on.
        if (!await collectionRepository.TryClaimWindowClosedLaNotificationAsync(collection.Id, utcNow, cancellationToken))
        {
            return false;
        }

        var outstanding = await outstandingSubmissionResolver.ResolveAsync(collection, allOrganisations, cancellationToken);

        if (outstanding.Count == 0)
        {
            // Nothing owed — every assigned organisation submitted. The claim above already stamped the marker,
            // so there is nothing further to persist.
            return true;
        }

        var contacts = await organisationUserRepository.GetActiveUsersForOrganisationsAsync(
            outstanding.Select(organisation => organisation.Id).ToList(),
            cancellationToken);

        var outstandingOrganisationIds = outstanding.Select(o => o.Id.Value).ToHashSet();

        var work = contacts
            .Where(contact => outstandingOrganisationIds.Contains(contact.OrganisationId))
            .Select(contact => (
                contact.Email,
                Notification: new CollectionWindowClosedLaNotification(contact.Name, collection.Name, deadline)))
            .ToList();

        var outcome = await EmailFanOut.SendAsync(
            work,
            (email, notification, ct) => emailService.SendWindowClosedAsync(email, notification, ct),
            "Window closed (LA)",
            collection.Id,
            settings.CollectionNotifications.MaxConcurrentReminderSends,
            logger,
            cancellationToken);

        // Release the claim when nothing was sent and nothing genuinely failed. Two conditions release it, and
        // both matter:
        //
        //   * NotConfigured — Notify has nothing to send WITH, which is not the same as a send that failed, so
        //     nothing is owed as "already sent"; the backlog goes out once DHSC creates the template.
        //   * cancellationToken.IsCancellationRequested — a shutdown that arrives before the fan-out sends even
        //     the first email leaves Sent=0/NotConfigured=false/HadOtherFailure=false, which would otherwise be
        //     indistinguishable from "sent successfully to nobody" and mark this collection done forever even
        //     though nothing went out. NotConfigured alone missed exactly this case.
        if (outcome.Sent == 0 && !outcome.HadOtherFailure && (outcome.NotConfigured || cancellationToken.IsCancellationRequested))
        {
            await collectionRepository.ReleaseWindowClosedLaNotificationClaimAsync(collection.Id, CancellationToken.None);

            if (outcome.NotConfigured)
            {
                logger.LogInformation(
                    "Notify is not configured for the window-closed LA notification; data collection {CollectionId} was not notified and will be retried on the next sweep.",
                    collection.Id.Value);
            }
            else
            {
                logger.LogInformation(
                    "Window-closed LA notification for data collection {CollectionId} was cancelled before any email was sent; it will be retried on the next sweep.",
                    collection.Id.Value);
            }

            return false;
        }

        logger.LogInformation(
            "Window-closed LA notification for data collection {CollectionId} sent to {Sent} of {Total} outstanding contact(s).",
            collection.Id.Value,
            outcome.Sent,
            work.Count);

        return true;
    }

    // The first date the notification is owed: the day after the window closed (BLUM-8399).
    //
    // A deadline is the last day LAs can submit, so a deadline window closes at the END of that day and the email is
    // due on deadline + 1. That is the same day GetStatus() starts answering Closed (and the DHSC closure notice
    // fires), which is deliberate: BLUM-8155 shipped this as deadline + 2 — a day after the first Closed date — so a
    // window with a 7 Oct deadline mailed LAs on the 9th, not the 8th the ticket asks for.
    //
    // A manual close happens DURING its day, so a day after it is ManuallyClosedAt's date + 1. Whichever path closed
    // the window first wins, so an early manual close is judged against its close date, never the original (possibly
    // weeks-away) deadline.
    private static DateOnly? GetNotificationDueDate(Collection collection)
    {
        var deadlineDue = collection.SubmissionDeadline?.AddDays(1);
        var manualDue = collection.ManuallyClosedAt is { } closedAt ? DateOnly.FromDateTime(closedAt).AddDays(1) : (DateOnly?)null;

        if (deadlineDue is null || (manualDue is not null && manualDue < deadlineDue))
        {
            return manualDue;
        }

        return deadlineDue;
    }

    private int ResolveMaxAgeDays()
    {
        var configured = settings.CollectionNotifications.MaxWindowClosedLaNotificationAgeDays;
        if (configured >= 1)
        {
            return configured;
        }

        // Clamped to 1. The deadline path needs SubmissionDeadline <= today - 1 (see
        // GetAwaitingWindowClosedLaNotificationAsync), so a floor below 1 makes that range empty and no
        // naturally-closed window is EVER notified — silently, so the feature looks alive. Read as "no limit", 0
        // would also remove the one guard against a restore-from-backup mailing every outstanding LA once per
        // historical window.
        logger.LogWarning(
            "MaxWindowClosedLaNotificationAgeDays is {Configured}, which would disable window-closed LA notifications entirely; using 1 day instead.",
            configured);

        return 1;
    }
}