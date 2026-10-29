using Application.Common.Interfaces;
using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.Common.Settings;
using Domain.Collections;
using Domain.Users;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Collections;

public class CollectionClosureNotifier(
    ICollectionQueries collectionQueries,
    ICollectionRepository collectionRepository,
    IUserRepository userRepository,
    ICollectionClosedEmailService emailService,
    IDateTimeProvider dateTimeProvider,
    ApplicationSettings settings,
    ILogger<CollectionClosureNotifier> logger)
    : ICollectionClosureNotifier
{
    public async Task<int> SweepAsync(CancellationToken cancellationToken = default)
    {
        // Same clock GetStatus() reads, so the SQL prefilter and the in-memory re-check agree.
        var today = DateOnly.FromDateTime(dateTimeProvider.GetUtcNow());
        var maxAgeDays = ResolveMaxAgeDays();

        var candidates = await collectionQueries.GetAwaitingClosureNotificationAsync(today, maxAgeDays, cancellationToken);
        if (candidates.Count == 0)
        {
            return 0;
        }

        // Read once per sweep, not once per collection: the active-admin list cannot change between two
        // collections in the same pass.
        var recipients = await userRepository.GetActiveAdminsAsync(cancellationToken);

        var notified = 0;
        foreach (var collection in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await NotifyIfClosedAsync(collection, recipients, cancellationToken))
            {
                notified++;
            }
        }

        if (notified > 0)
        {
            logger.LogInformation("Sent the closure notification for {Count} data collection(s).", notified);
        }

        return notified;
    }

    public async Task NotifyAsync(Collection collection, CancellationToken cancellationToken = default)
    {
        var recipients = await userRepository.GetActiveAdminsAsync(cancellationToken);

        await NotifyIfClosedAsync(collection, recipients, cancellationToken);
    }

    private async Task<bool> NotifyIfClosedAsync(
        Collection collection,
        IReadOnlyList<User> recipients,
        CancellationToken cancellationToken)
    {
        var utcNow = dateTimeProvider.GetUtcNow();

        if (collection.ClosureNotifiedAt is not null)
        {
            return false;
        }

        // GetStatus() is the authority on what "closed" means; the query predicate is only a prefilter, and
        // NotifyAsync is handed whatever the close handler was holding.
        if (!collection.GetStatus(utcNow).Equals(CollectionStatus.Closed))
        {
            return false;
        }

        // AC2: the reporting period, not the collection window. A published collection always has both, but a
        // draft that was published before those fields were mandatory could still be missing them — and an email
        // saying the report covers "01/01/0001" is worse than no email.
        if (collection.ReportingPeriodStart is null || collection.ReportingPeriodEnd is null)
        {
            logger.LogWarning(
                "Data collection {CollectionId} closed without a reporting period; closure notification not sent.",
                collection.Id.Value);
            return false;
        }

        var delivered = 0;
        var notConfigured = false;
        foreach (var recipient in recipients)
        {
            // Built per-recipient, not once for the loop: the live template greets the admin by name, so
            // RecipientName varies even though everything else here is the same for every DHSC Super Admin.
            var notification = new CollectionClosureNotification(
                collection.Name,
                recipient.Name.ToString(),
                collection.ReportingPeriodStart.Value,
                collection.ReportingPeriodEnd.Value);

            var result = await emailService.SendClosureAsync(recipient.Email, notification, cancellationToken);

            if (result.IsRight)
            {
                delivered++;
                continue;
            }

            result.IfLeft(exception =>
            {
                if (exception is EmailNotConfiguredException)
                {
                    notConfigured = true;
                    return;
                }

                logger.LogWarning(
                    exception,
                    "Closure notification for data collection {CollectionId} failed for {Email}.",
                    collection.Id.Value,
                    recipient.Email);
            });

            // The refusal is about the configuration, not this recipient, so every remaining send would be refused
            // identically. Stopping here is what keeps the shipping state (no template yet) from emitting one
            // warning per admin per collection on every six-hourly sweep for the whole retry window.
            if (notConfigured)
            {
                break;
            }
        }

        // Notify has nothing to send WITH, which is not the same as a send that failed — so nothing is owed as
        // "already sent". Leave the marker clear and let the next sweep try again, so the backlog goes out once
        // DHSC creates the template and Notify__CollectionClosedTemplateId is set. Information, not Warning: this
        // is the documented state the feature ships in, and it recurs every sweep until the template exists.
        if (notConfigured)
        {
            logger.LogInformation(
                "Notify is not configured for the closure notification; data collection {CollectionId} was not notified and will be retried on the next sweep.",
                collection.Id.Value);
            return false;
        }

        // Stamped once per collection whatever the individual sends did — including a send that reached nobody at
        // all. Retrying only the failures would mean re-sending to everyone who already received it, because the
        // marker is per collection, not per recipient, and a duplicate "your window has closed" to every DHSC
        // admin is a worse outcome than one missed mail that the log already records. That reasoning holds even
        // when NOTHING was delivered: NotifyEmailServiceBase.SendAsync bounds the wait but cannot abort the HTTP
        // call the Notify SDK has already started, so a degraded-but-working Notify returns a Left for mail that
        // is genuinely delivered — and retrying that would mail every admin twice.
        //
        // CancellationToken.None on purpose: emails have already gone out by this line. A caller who closed a
        // window and then dropped the socket would otherwise cancel the write that records it, and the next sweep
        // would mail everyone a second time — the exact duplicate the marker exists to prevent.
        //
        // Read again here rather than reusing the instant the status was judged against: the sends above can take
        // minutes on a degraded Notify, and this records when they finished.
        collection.MarkClosureNotified(dateTimeProvider.GetUtcNow());
        await collectionRepository.UpdateAsync(collection, CancellationToken.None);

        logger.LogInformation(
            "Closure notification for data collection {CollectionId} sent to {Sent} of {Total} active DHSC admin(s).",
            collection.Id.Value,
            delivered,
            recipients.Count);

        return true;
    }

    private int ResolveMaxAgeDays()
    {
        var configured = settings.CollectionNotifications.MaxClosureNotificationAgeDays;
        if (configured >= 1)
        {
            return configured;
        }

        // Clamped rather than honoured, because neither reading of a non-positive value is safe to guess at. Taken
        // literally the window is empty and no window that closed on its own is EVER notified — silently, with the
        // manual close still working, so the feature looks alive. Read as "no limit" it removes the one guard
        // against a restore-from-backup mailing every admin once per historical window. Warn and use a day.
        logger.LogWarning(
            "MaxClosureNotificationAgeDays is {Configured}, which would disable deadline-expiry notifications entirely; using 1 day instead.",
            configured);

        return 1;
    }
}