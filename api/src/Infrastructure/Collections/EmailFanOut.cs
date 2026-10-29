using System.Collections.Concurrent;
using Application.Common.Interfaces;
using Domain.Collections;
using LanguageExt;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Collections;

// What one half of a fan-out actually did. NotConfigured and HadOtherFailure are NOT opposites and both matter:
// "refused because there is no template" is retryable, "attempted and failed" is not, and "had nothing to do" is
// neither. Collapsing them loses the distinction the claim-release branch in each caller turns on.
internal readonly record struct SendOutcome(int Sent, bool NotConfigured, bool HadOtherFailure);

// Bounded-concurrency email fan-out, shared by CollectionReminderNotifier (BLUM-7571 deadline reminder + BLUM-7721
// window-opened) and CollectionWindowClosedLaNotifier (BLUM-8155), so the three cannot drift on concurrency, the
// not-configured short-circuit, or the cancellation rule.
//
// Bounded concurrency, following BulkImportLocalAuthoritiesCommand. A "submitted by all LAs" window is several
// hundred sends; one at a time that is minutes, and against a degraded Notify (15s per send) it is hours — long
// enough for App Service to unload the site mid-fan-out. maxConcurrentSends set to 1 restores fully sequential
// behaviour.
//
// Nothing in here touches a DbContext: every caller reads before this runs and writes after it returns.
// DbContext is not thread-safe and, unlike the bulk import, there is no per-worker DI scope here — so a stray
// repository call added inside these workers would be a real bug.
internal static class EmailFanOut
{
    private const int MinWorkers = 1;
    private const int MaxWorkers = 16;

    public static async Task<SendOutcome> SendAsync<TNotification>(
        IReadOnlyList<(string Email, TNotification Notification)> work,
        Func<string, TNotification, CancellationToken, Task<Either<Exception, Unit>>> send,
        string logLabel,
        CollectionId collectionId,
        int maxConcurrentSends,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (work.Count == 0)
        {
            return default;
        }

        var queue = new ConcurrentQueue<(string Email, TNotification Notification)>(work);
        var sent = 0;
        var notConfigured = 0;
        var otherFailures = 0;

        var workerCount = Math.Min(
            Math.Clamp(maxConcurrentSends, MinWorkers, MaxWorkers),
            work.Count);

        var workers = Enumerable.Range(0, workerCount).Select(async _ =>
        {
            while (queue.TryDequeue(out var item))
            {
                // A refusal about configuration is identical for every remaining recipient, so stop the whole
                // fan-out rather than logging one warning per organisation per sweep for the entire retry
                // window — on an all-LAs window that is 153 warnings, not 3.
                if (Volatile.Read(ref notConfigured) > 0)
                {
                    return;
                }

                // Stop dequeuing rather than throwing. A throw here would escape the caller before its claim row
                // (or idempotency marker) is stamped, permanently suppressing a send that only partly went out.
                // Returning instead lets the counts come back so the caller records honestly how far it got.
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                var result = await send(item.Email, item.Notification, cancellationToken);

                if (result.IsRight)
                {
                    Interlocked.Increment(ref sent);
                    continue;
                }

                result.IfLeft(exception =>
                {
                    if (exception is EmailNotConfiguredException)
                    {
                        Interlocked.Increment(ref notConfigured);
                        return;
                    }

                    Interlocked.Increment(ref otherFailures);
                    logger.LogWarning(
                        exception,
                        "{Label} for data collection {CollectionId} failed for {Email}.",
                        logLabel,
                        collectionId.Value,
                        item.Email);
                });
            }
        }).ToList();

        await Task.WhenAll(workers);

        return new SendOutcome(sent, notConfigured > 0, otherFailures > 0);
    }
}