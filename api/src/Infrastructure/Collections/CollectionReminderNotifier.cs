using Application.Collections;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.Common.Settings;
using Application.Submissions;
using Domain.Collections;
using Domain.Organisations;
using Domain.Users;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Collections;

public class CollectionReminderNotifier(
    ICollectionQueries collectionQueries,
    ICollectionReminderDispatchQueries dispatchQueries,
    ICollectionReminderDispatchRepository dispatchRepository,
    IOrganisationQueries organisationQueries,
    IOrganisationUserRepository organisationUserRepository,
    OutstandingSubmissionResolver outstandingSubmissionResolver,
    IUserRepository userRepository,
    ICollectionReminderEmailService reminderEmailService,
    ICollectionReminderSummaryEmailService summaryEmailService,
    ICollectionWindowOpenedEmailService windowOpenedEmailService,
    ICollectionWindowOpenedSummaryEmailService windowOpenedSummaryEmailService,
    IDateTimeProvider dateTimeProvider,
    ApplicationSettings settings,
    ILogger<CollectionReminderNotifier> logger)
    : ICollectionReminderNotifier
{
    public async Task<int> SweepAsync(CancellationToken cancellationToken = default)
    {
        var utcNow = dateTimeProvider.GetUtcNow();

        // Same clock GetStatus() reads, so the SQL prefilter, the in-memory re-check and ReminderSchedule agree.
        var today = DateOnly.FromDateTime(utcNow);
        var maxAgeDays = ResolveMaxAgeDays();

        var candidates = await collectionQueries.GetOpenForNotificationsAsync(today, cancellationToken);
        if (candidates.Count == 0)
        {
            return 0;
        }

        // Read once per sweep, not once per collection: neither the organisation estate nor the active-admin list
        // can change between two collections in the same pass, and on a "submitted by all LAs" window the first
        // of these is the whole ~153-organisation estate.
        var allOrganisations = await organisationQueries.GetOrganisations(cancellationToken);
        var dhscRecipients = await userRepository.GetActiveAdminsAsync(cancellationToken);

        var candidateIds = candidates.Select(c => c.Id).ToList();

        var claimed = (await dispatchQueries.GetClaimedScheduledAsync(
                candidateIds,
                today.AddDays(-maxAgeDays),
                cancellationToken))
            .Select(row => (row.CollectionId, row.ScheduledFor))
            .ToHashSet();

        // Keyed on the collection alone, not on a date — see ICollectionReminderDispatchQueries for why the
        // window-open announcement is once per collection while a reminder is once per trigger point.
        var windowAnnounced = (await dispatchQueries.GetCollectionsWithWindowOpenedClaimAsync(
                candidateIds,
                cancellationToken))
            .ToHashSet();

        var dispatched = 0;
        foreach (var collection in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // GetStatus() is the authority on what "open" means; the query predicate is only a prefilter. It
            // covers draft, manual close, a passed deadline and a future open date, for BOTH passes below.
            if (!collection.GetStatus(utcNow).Equals(CollectionStatus.Open))
            {
                continue;
            }

            var sentSomething = false;

            // ---- BLUM-7721: the window-open announcement, FIRST ----
            //
            // Above the deadline pass's own `continue`s on purpose. A collection with no configured reminders is
            // exactly the population the relaxed GetOpenForNotificationsAsync exists to reach, and DueOn returns
            // nothing for it — so a window-open pass placed after those `continue`s would never run for the
            // collections that need it most, and no test would say so.
            //
            // It also runs first because it wins a tie: see the suppression below.
            //
            // Sequentially, never concurrently with the deadline pass. Both write through the one scoped
            // DbContext, which is not thread-safe, and both fan out through a worker pool sized for one caller.
            var openedOn = ReminderSchedule.WindowOpenedOn(collection, today);
            var announced = openedOn is not null && windowAnnounced.Contains(collection.Id.Value);

            if (openedOn is { } windowOpenDate && !announced)
            {
                var outcome = await DispatchWindowOpenedAsync(
                    collection,
                    windowOpenDate,
                    allOrganisations,
                    dhscRecipients,
                    cancellationToken);

                announced = outcome.Claimed;
                sentSomething |= outcome.Dispatched;
            }

            // ---- BLUM-7571: the deadline reminders ----
            //
            // The LAST due fire date, not every one. If the service was unloaded across two configured reminder
            // dates, the outstanding organisations get one email — the more urgent — rather than two identical
            // ones a second apart. The superseded date never gets a dispatch row, which is honest: no reminder
            // was sent for it.
            var due = ReminderSchedule.DueOn(collection, today, maxAgeDays).LastOrDefault();

            if (due != default && !claimed.Contains((collection.Id.Value, due.ScheduledFor)))
            {
                // The window-open announcement covers this day already, so the reminder is not sent — "the
                // reporting window is now open" and "you have not submitted yet" on one morning contradict each
                // other. A configured "0 days after the collection opens" reminder resolves to exactly this date,
                // which is why ReminderSchedule.DueOn's pre-open filter is >= (BLUM-7571 left it that way for
                // this ticket).
                //
                // The trigger point is still CLAIMED rather than skipped. Skipping it leaves it due, and the next
                // sweep six hours later would send the reminder anyway — both emails, further apart, which is
                // worse than either outcome. The row records what happened: claimed, nothing sent.
                var suppressed = announced && due.ScheduledFor == openedOn;

                var result = await DispatchAsync(
                    collection,
                    ReminderTrigger.Scheduled,
                    due.ScheduledFor,
                    due.OffsetType,
                    due.Days,
                    allOrganisations,
                    dhscRecipients,
                    suppressed,
                    cancellationToken);

                sentSomething |= result is not null;
            }

            if (sentSomething)
            {
                dispatched++;
            }
        }

        if (dispatched > 0)
        {
            // Collections, not dispatch rows: one collection can now contribute both an announcement and a
            // reminder in the same pass, and this number has always meant "how many windows did something go out
            // for".
            logger.LogInformation("Sent collection notifications for {Count} data collection(s).", dispatched);
        }

        return dispatched;
    }

    public async Task<CollectionReminderResult> SendManualAsync(
        Collection collection,
        CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(dateTimeProvider.GetUtcNow());

        var allOrganisations = await organisationQueries.GetOrganisations(cancellationToken);
        var dhscRecipients = await userRepository.GetActiveAdminsAsync(cancellationToken);

        var result = await DispatchAsync(
            collection,
            ReminderTrigger.Manual,
            today,
            offsetType: null,
            days: null,
            allOrganisations,
            dhscRecipients,
            suppressed: false,
            cancellationToken);

        return result ?? default;
    }

    // Returns null when nothing was dispatched — the claim was lost to another instance, or every send was
    // refused because Notify is not configured for this purpose yet.
    private async Task<CollectionReminderResult?> DispatchAsync(
        Collection collection,
        ReminderTrigger trigger,
        DateOnly scheduledFor,
        string? offsetType,
        int? days,
        IReadOnlyList<Organisation> allOrganisations,
        IReadOnlyList<User> dhscRecipients,
        bool suppressed,
        CancellationToken cancellationToken)
    {
        if (collection.SubmissionDeadline is null)
        {
            // AC2 needs the deadline in the body. SweepAsync cannot reach here (ReminderSchedule.DueOn already
            // refuses), but SendManualAsync is handed whatever the handler was holding.
            logger.LogWarning(
                "Data collection {CollectionId} has no submission deadline; reminder not sent.",
                collection.Id.Value);
            return null;
        }

        // The live Notify template also needs the reporting period, same as the window-opened announcement
        // (DispatchWindowOpenedAsync) requires it — see that method for why this is a hard refusal rather than a
        // blank personalisation value.
        if (collection.ReportingPeriodStart is not { } periodStart || collection.ReportingPeriodEnd is not { } periodEnd)
        {
            logger.LogWarning(
                "Data collection {CollectionId} has no reporting period configured; reminder not sent.",
                collection.Id.Value);
            return null;
        }

        var deadline = collection.SubmissionDeadline.Value;
        var periodLabel = CollectionPeriodLabel.For(collection);

        var outstanding = await outstandingSubmissionResolver.ResolveAsync(collection, allOrganisations, cancellationToken);

        // Claim BEFORE any email leaves. App Service overlaps instances during a deploy, and both would otherwise
        // fan the same reminder out to the same organisations; the unique index refuses the loser here instead of
        // after several hundred duplicate emails. This is the deliberate inversion of BLUM-7719's send-then-stamp,
        // which is right there because a duplicate reaches a handful of DHSC admins, and wrong here because it
        // reaches every LA in the country.
        var dispatch = CollectionReminderDispatch.Create(
            CollectionReminderDispatchId.New(),
            collection.Id,
            trigger,
            scheduledFor,
            dateTimeProvider.GetUtcNow(),
            offsetType,
            days);

        try
        {
            await dispatchRepository.AddAsync(dispatch, cancellationToken);
        }
        catch (Exception exception) when (Contains<DbUpdateException>(exception))
        {
            // Another instance already claimed this trigger point. Debug, not Warning: during a deploy this is
            // the mechanism working, not a fault.
            dispatchRepository.ForgetTrackedChanges();
            logger.LogDebug(
                exception,
                "Reminder for data collection {CollectionId} on {ScheduledFor} was already claimed elsewhere.",
                collection.Id.Value,
                scheduledFor);
            return null;
        }

        // A suppressed trigger point is claimed and stamped without sending anything: the window-open
        // announcement already went to these organisations today (BLUM-7721). Claiming it is what stops the next
        // sweep, six hours later, from finding it still due and sending the reminder after all.
        if (suppressed)
        {
            dispatch.MarkSent(outstanding.Count, 0, 0, dateTimeProvider.GetUtcNow());
            await dispatchRepository.UpdateAsync(dispatch, CancellationToken.None);

            logger.LogInformation(
                "Deadline reminder for data collection {CollectionId} on {ScheduledFor} was superseded by the window-open notification and not sent.",
                collection.Id.Value,
                scheduledFor);

            // Null because nothing was dispatched — which is the truth, and keeps the sweep's count honest even
            // when the announcement that superseded this went out on an earlier sweep.
            return null;
        }

        var provider = await SendProviderRemindersAsync(
            collection, periodLabel, periodStart, periodEnd, deadline, outstanding, cancellationToken);

        var dhsc = await SendDhscSummariesAsync(
            collection, periodLabel, periodStart, periodEnd, deadline, outstanding.Count, dhscRecipients, cancellationToken);

        var providerSent = provider.Sent;
        var dhscSent = dhsc.Sent;

        // Notify having nothing to send WITH is not a send that failed, so nothing is owed as "already sent" —
        // release the claim and let the next sweep try again, so the backlog goes out once DHSC creates the
        // templates and the Notify__* settings are filled in. This is the state the feature ships in, hence
        // Information rather than Warning: it recurs every sweep until then.
        //
        // Three conditions, and each is load-bearing:
        //
        //   * nothing was delivered — releasing after a successful send would re-mail those recipients;
        //   * neither half suffered a real send FAILURE — NotifyEmailServiceBase bounds how long it waits but
        //     cannot abort an HTTP call the SDK has started, so a "failure" may be mail that genuinely arrived,
        //     and retrying it would duplicate;
        //   * at least one half was refused for missing configuration.
        //
        // Testing "both halves reported not-configured" instead is what the first version did, and it was wrong:
        // a half with nothing to do reports NOTHING (no outstanding organisations, or none with active contacts),
        // so a window where everybody had already submitted kept its claim, consumed the trigger point, and never
        // sent AC5's summary once the template existed. HadOtherFailure is what keeps the two apart.
        if (providerSent == 0
            && dhscSent == 0
            && !provider.HadOtherFailure
            && !dhsc.HadOtherFailure
            && (provider.NotConfigured || dhsc.NotConfigured))
        {
            await dispatchRepository.RemoveAsync(dispatch, CancellationToken.None);
            logger.LogInformation(
                "Notify is not configured for deadline reminders; data collection {CollectionId} was not reminded and will be retried on the next sweep.",
                collection.Id.Value);
            return null;
        }

        if (provider.NotConfigured || dhsc.NotConfigured)
        {
            logger.LogWarning(
                "Only part of the reminder for data collection {CollectionId} could be sent: provider template configured = {ProviderConfigured}, DHSC template configured = {DhscConfigured}.",
                collection.Id.Value,
                !provider.NotConfigured,
                !dhsc.NotConfigured);
        }

        // CancellationToken.None on purpose: emails have already gone out by this line. A caller who pressed the
        // manual button and then dropped the socket would otherwise cancel the write that records it, and the
        // claim row would sit unstamped forever.
        dispatch.MarkSent(outstanding.Count, providerSent, dhscSent, dateTimeProvider.GetUtcNow());
        await dispatchRepository.UpdateAsync(dispatch, CancellationToken.None);

        logger.LogInformation(
            "Reminder for data collection {CollectionId} ({ScheduledFor}, {Trigger}) sent to {ProviderSent} contact(s) across {Outstanding} outstanding organisation(s), and {DhscSent} of {DhscTotal} DHSC admin(s).",
            collection.Id.Value,
            scheduledFor,
            trigger.ToString(),
            providerSent,
            outstanding.Count,
            dhscSent,
            dhscRecipients.Count);

        return new CollectionReminderResult(outstanding.Count, providerSent, dhscSent);
    }

    // What the window-open pass did (BLUM-7721).
    //
    // Claimed and Dispatched are NOT the same question, and the suppression rule turns on the first. Claimed
    // means THIS instance wrote a window_opened row and kept it: the announcement went out, so a reminder falling
    // on the same day must stand down. It is deliberately FALSE both when the claim was RELEASED because Notify
    // has no template for this purpose yet, and when another instance held the row at that instant — in neither
    // case is anything known to have been announced, and letting the deadline reminder through is the better of
    // the two failures, because the organisation hears something.
    private readonly record struct WindowOpenedOutcome(bool Dispatched, bool Claimed);

    private async Task<WindowOpenedOutcome> DispatchWindowOpenedAsync(
        Collection collection,
        DateOnly openedOn,
        IReadOnlyList<Organisation> allOrganisations,
        IReadOnlyList<User> dhscRecipients,
        CancellationToken cancellationToken)
    {
        // ReminderSchedule.WindowOpenedOn has already refused a collection missing any of these, so this is a
        // restatement for the compiler rather than a second guard.
        if (collection.SubmissionDeadline is not { } deadline
            || collection.ReportingPeriodStart is not { } periodStart
            || collection.ReportingPeriodEnd is not { } periodEnd)
        {
            return default;
        }

        // Every assigned organisation, NOT just the ones that have not submitted. AC1 says "each organisation in
        // the collection", and this is an announcement rather than a chase — it carries no "you have not
        // submitted" claim to be wrong about. Note the tempting justification "nobody has submitted at window
        // open" is false: the announcement is owed however long ago the window opened, so by the time it goes out
        // some organisations may well have submitted already.
        //
        // Suspended organisations are still excluded, for the same reason OutstandingSubmissionResolver excludes them:
        // OrganisationSuspendedMiddleware blocks their users at the door, so telling them the window is open and
        // to sign in is unactionable.
        var assigned = SubmissionAssignment.ResolveAssignedOrganisations(collection, allOrganisations)
            .Where(organisation => !organisation.OnboardingStatus.Equals(OnboardingStatus.Suspended))
            .ToList();

        var periodLabel = CollectionPeriodLabel.For(collection);

        // Claim BEFORE any email leaves, for the same reason the reminder path does — and more so: this fan-out
        // has no already-submitted filter to shrink it, so it is the largest one the system performs.
        var dispatch = CollectionReminderDispatch.Create(
            CollectionReminderDispatchId.New(),
            collection.Id,
            ReminderTrigger.WindowOpened,
            openedOn,
            dateTimeProvider.GetUtcNow());

        try
        {
            await dispatchRepository.AddAsync(dispatch, cancellationToken);
        }
        catch (Exception exception) when (Contains<DbUpdateException>(exception))
        {
            // Another instance holds the claim as of this instant. Reported as NOT claimed, deliberately — the
            // winner may still release it, because an unconfigured template is exactly the state this ships in.
            // Suppressing the same-day reminder on the strength of a claim that is about to be deleted would
            // consume that trigger point having sent nothing at all, and neither email would go out.
            //
            // The cost of the other reading is that a same-day reminder goes out alongside the winner's
            // announcement — mildly redundant, and the same "the organisation hears something" preference the
            // release branch below is built on. A claim that survived an earlier sweep is committed and arrives
            // through windowAnnounced instead, which is the path that does suppress.
            dispatchRepository.ForgetTrackedChanges();
            logger.LogDebug(
                exception,
                "The window-open notification for data collection {CollectionId} was already claimed elsewhere.",
                collection.Id.Value);
            return default;
        }

        var provider = await SendWindowOpenedNoticesAsync(
            collection, periodLabel, periodStart, periodEnd, deadline, assigned, cancellationToken);

        // The DHSC summary is NOT attempted when the provider template is missing, and the claim is released
        // before it can be. This is where the announcement diverges from the deadline reminder, because its claim
        // is once per collection and permanent: if the summary sent while the provider half was refused for
        // configuration, the general release below would not fire (something WAS delivered), the row would be
        // stamped, and the unique index would make it impossible ever to tell that collection's organisations
        // their window had opened. AC1 is the primary criterion here; losing it is not a partial success.
        //
        // Skipping the summary rather than sending and releasing also stops the retry re-mailing DHSC admins four
        // times a day for as long as the other template is missing. The reverse case — provider configured,
        // summary not — still falls through to the partial-config warning below and keeps the claim, because
        // releasing there would re-announce to every organisation that already heard.
        if (provider.NotConfigured)
        {
            await dispatchRepository.RemoveAsync(dispatch, CancellationToken.None);
            logger.LogInformation(
                "Notify is not configured for the window-open notification; data collection {CollectionId} was not announced and will be retried on the next sweep.",
                collection.Id.Value);

            return default;
        }

        var dhsc = await SendDhscWindowOpenedSummariesAsync(
            collection, periodLabel, periodStart, periodEnd, deadline, assigned.Count, dhscRecipients, cancellationToken);

        // The same three-condition release as the reminder path, and each condition is load-bearing for the same
        // reasons — see DispatchAsync. This is the state the feature ships in (both template ids are empty until
        // DHSC creates the templates), hence Information rather than Warning.
        if (provider.Sent == 0
            && dhsc.Sent == 0
            && !provider.HadOtherFailure
            && !dhsc.HadOtherFailure
            && (provider.NotConfigured || dhsc.NotConfigured))
        {
            await dispatchRepository.RemoveAsync(dispatch, CancellationToken.None);
            logger.LogInformation(
                "Notify is not configured for the window-open notification; data collection {CollectionId} was not announced and will be retried on the next sweep.",
                collection.Id.Value);

            // Not claimed: nothing was announced, so a deadline reminder falling today should still go out.
            return default;
        }

        if (provider.NotConfigured || dhsc.NotConfigured)
        {
            logger.LogWarning(
                "Only part of the window-open notification for data collection {CollectionId} could be sent: provider template configured = {ProviderConfigured}, DHSC template configured = {DhscConfigured}.",
                collection.Id.Value,
                !provider.NotConfigured,
                !dhsc.NotConfigured);
        }

        // OutstandingOrganisationCount is recorded as 0, not as the number announced to. The column means what
        // its name says — how many organisations had not submitted when a REMINDER went out — and at a window
        // opening that question has not been asked. The volumes that matter here are the two email counts.
        dispatch.MarkSent(0, provider.Sent, dhsc.Sent, dateTimeProvider.GetUtcNow());
        await dispatchRepository.UpdateAsync(dispatch, CancellationToken.None);

        logger.LogInformation(
            "Window-open notification for data collection {CollectionId} ({OpenedOn}) sent to {ProviderSent} contact(s) across {Assigned} organisation(s), and {DhscSent} of {DhscTotal} DHSC admin(s).",
            collection.Id.Value,
            openedOn,
            provider.Sent,
            assigned.Count,
            dhsc.Sent,
            dhscRecipients.Count);

        return new WindowOpenedOutcome(Dispatched: true, Claimed: true);
    }

    private async Task<SendOutcome> SendProviderRemindersAsync(
        Collection collection,
        string periodLabel,
        DateOnly periodStart,
        DateOnly periodEnd,
        DateOnly deadline,
        IReadOnlyList<Organisation> outstanding,
        CancellationToken cancellationToken)
    {
        if (outstanding.Count == 0)
        {
            return default;
        }

        var contacts = await organisationUserRepository.GetActiveUsersForOrganisationsAsync(
            outstanding.Select(organisation => organisation.Id).ToList(),
            cancellationToken);

        var outstandingOrganisationIds = outstanding.Select(o => o.Id.Value).ToHashSet();

        var work = contacts
            .Where(contact => outstandingOrganisationIds.Contains(contact.OrganisationId))
            .Select(contact => (
                contact.Email,
                Notification: new CollectionReminderNotification(
                    collection.Name,
                    contact.Name,
                    periodLabel,
                    periodStart,
                    periodEnd,
                    deadline)))
            .ToList();

        return await EmailFanOut.SendAsync(
            work,
            (email, notification, ct) => reminderEmailService.SendReminderAsync(email, notification, ct),
            "Deadline reminder",
            collection.Id,
            settings.CollectionNotifications.MaxConcurrentReminderSends,
            logger,
            cancellationToken);
    }

    private async Task<SendOutcome> SendWindowOpenedNoticesAsync(
        Collection collection,
        string periodLabel,
        DateOnly periodStart,
        DateOnly periodEnd,
        DateOnly deadline,
        IReadOnlyList<Organisation> assigned,
        CancellationToken cancellationToken)
    {
        if (assigned.Count == 0)
        {
            return default;
        }

        var contacts = await organisationUserRepository.GetActiveUsersForOrganisationsAsync(
            assigned.Select(organisation => organisation.Id).ToList(),
            cancellationToken);

        var assignedOrganisationIds = assigned.Select(o => o.Id.Value).ToHashSet();

        var work = contacts
            .Where(contact => assignedOrganisationIds.Contains(contact.OrganisationId))
            .Select(contact => (
                contact.Email,
                Notification: new CollectionWindowOpenedNotification(
                    collection.Name,
                    contact.Name,
                    periodLabel,
                    periodStart,
                    periodEnd,
                    deadline)))
            .ToList();

        return await EmailFanOut.SendAsync(
            work,
            (email, notification, ct) => windowOpenedEmailService.SendWindowOpenedAsync(email, notification, ct),
            "Window-open notification",
            collection.Id,
            settings.CollectionNotifications.MaxConcurrentReminderSends,
            logger,
            cancellationToken);
    }

    private async Task<SendOutcome> SendDhscWindowOpenedSummariesAsync(
        Collection collection,
        string periodLabel,
        DateOnly periodStart,
        DateOnly periodEnd,
        DateOnly deadline,
        int assignedCount,
        IReadOnlyList<User> recipients,
        CancellationToken cancellationToken)
    {
        if (recipients.Count == 0)
        {
            return default;
        }

        // Sequential: this is a handful of admins, exactly as SendDhscSummariesAsync does it.
        var sent = 0;
        var notConfigured = false;
        var otherFailure = false;
        var isMaturityAssessment = SubmissionEligibility.IsMaturityAssessment(collection);

        foreach (var recipient in recipients)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            // Built per-recipient, not once for the loop: the live template greets the admin by name, so
            // RecipientName varies even though everything else here is the same for every DHSC Super Admin. AC2
            // is unconditional — the summary goes out whenever a window opens, including one with no
            // organisations assigned to it yet, which is why assignedCount is carried rather than used as a guard.
            var summary = new CollectionWindowOpenedSummary(
                collection.Name, recipient.Name.ToString(), periodLabel, periodStart, periodEnd, deadline, assignedCount, isMaturityAssessment);

            var result = await windowOpenedSummaryEmailService.SendWindowOpenedSummaryAsync(
                recipient.Email, summary, cancellationToken);

            if (result.IsRight)
            {
                sent++;
                continue;
            }

            result.IfLeft(exception =>
            {
                if (exception is EmailNotConfiguredException)
                {
                    notConfigured = true;
                    return;
                }

                otherFailure = true;
                logger.LogWarning(
                    exception,
                    "Window-open summary for data collection {CollectionId} failed for {Email}.",
                    collection.Id.Value,
                    recipient.Email);
            });

            if (notConfigured)
            {
                break;
            }
        }

        return new SendOutcome(sent, notConfigured, otherFailure);
    }

    private async Task<SendOutcome> SendDhscSummariesAsync(
        Collection collection,
        string periodLabel,
        DateOnly periodStart,
        DateOnly periodEnd,
        DateOnly deadline,
        int outstandingCount,
        IReadOnlyList<User> recipients,
        CancellationToken cancellationToken)
    {
        if (recipients.Count == 0)
        {
            return default;
        }

        // Sequential: this is a handful of admins, exactly as CollectionClosureNotifier does it.
        var sent = 0;
        var notConfigured = false;
        var otherFailure = false;
        var isMaturityAssessment = SubmissionEligibility.IsMaturityAssessment(collection);

        foreach (var recipient in recipients)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            // Built per-recipient, not once for the loop: the live template greets the admin by name, so
            // RecipientName varies even though everything else here is the same for every DHSC Super Admin.
            // AC5 is unconditional — the summary goes out whenever the trigger point is reached, including when
            // every organisation has already submitted and outstandingCount is 0. That number is what keeps AC6's
            // "a reminder has been sent to outstanding local authorities" honest, and the template needs a branch
            // for it.
            var summary = new CollectionReminderSummary(
                collection.Name, recipient.Name.ToString(), periodLabel, periodStart, periodEnd, deadline, outstandingCount, isMaturityAssessment);

            var result = await summaryEmailService.SendReminderSummaryAsync(recipient.Email, summary, cancellationToken);

            if (result.IsRight)
            {
                sent++;
                continue;
            }

            result.IfLeft(exception =>
            {
                if (exception is EmailNotConfiguredException)
                {
                    notConfigured = true;
                    return;
                }

                otherFailure = true;
                logger.LogWarning(
                    exception,
                    "Reminder summary for data collection {CollectionId} failed for {Email}.",
                    collection.Id.Value,
                    recipient.Email);
            });

            if (notConfigured)
            {
                break;
            }
        }

        return new SendOutcome(sent, notConfigured, otherFailure);
    }

    private int ResolveMaxAgeDays()
    {
        var configured = settings.CollectionNotifications.MaxReminderNotificationAgeDays;
        if (configured >= 1)
        {
            return configured;
        }

        // Clamped rather than honoured, for the same two reasons as CollectionClosureNotifier's twin. Taken
        // literally the catch-up window is empty and no reminder is EVER sent by the sweep — silently, with the
        // manual button still working, so the feature looks alive. Read as "no limit" it removes the one guard
        // against a restore-from-backup mailing every outstanding organisation once per historical fire date.
        logger.LogWarning(
            "MaxReminderNotificationAgeDays is {Configured}, which would disable scheduled reminders entirely; using 1 day instead.",
            configured);

        return 1;
    }

    // RepositoryBase.AddAsync wraps everything it catches in a plain Exception, so the unique-index violation
    // arrives as an inner exception rather than as a DbUpdateException at the top.
    private static bool Contains<TException>(Exception exception)
        where TException : Exception
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is TException)
            {
                return true;
            }
        }

        return false;
    }
}