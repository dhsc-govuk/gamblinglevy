using System.Collections.Concurrent;
using Application.Common.Interfaces;
using LanguageExt;

namespace Tests.Common.Services;

// Test double for ICollectionReminderEmailService (BLUM-7571). Records the whole notification rather than just
// the address, because AC2 is about what the email SAYS — the collection name and the deadline date — and a
// recorder that only kept recipients would let that be asserted vacuously.
//
// Mirrors InMemoryCollectionClosedEmailService, including its two independent switches: FailFor makes sends for
// one named collection return a plain Left, NotConfiguredFor makes them return the EmailNotConfiguredException
// Left specifically. CollectionReminderNotifier treats those as opposites — a failed send keeps the dispatch
// claim so the fan-out is never repeated, an unconfigured template releases it so the next sweep retries — and a
// fake that could only produce one of them would let that distinction be deleted with every test still green.
public class InMemoryCollectionReminderEmailService : ICollectionReminderEmailService
{
    private static readonly ConcurrentBag<(string Email, string CollectionName, DateOnly SubmissionDeadline, string RecipientName)> _sent = new();

    // Keyed by collection name rather than a global on/off flag: two test classes run at a time, so a flag would
    // make every OTHER class's reminder sends fail for as long as one test held it.
    private static readonly ConcurrentDictionary<string, byte> _failing = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, byte> _notConfigured = new(StringComparer.Ordinal);

    public Task<Either<Exception, Unit>> SendReminderAsync(
        string email,
        CollectionReminderNotification notification,
        CancellationToken cancellationToken)
    {
        if (_notConfigured.ContainsKey(notification.CollectionName))
        {
            return Task.FromResult<Either<Exception, Unit>>(
                new EmailNotConfiguredException("Simulated missing Notify configuration."));
        }

        if (_failing.ContainsKey(notification.CollectionName))
        {
            return Task.FromResult<Either<Exception, Unit>>(new Exception("Simulated Notify failure."));
        }

        _sent.Add((email, notification.CollectionName, notification.SubmissionDeadline, notification.RecipientName));
        return Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }

    public static void FailFor(string collectionName) => _failing[collectionName] = 0;

    public static void StopFailingFor(string collectionName) => _failing.TryRemove(collectionName, out _);

    public static void NotConfiguredFor(string collectionName) => _notConfigured[collectionName] = 0;

    public static void ConfigureFor(string collectionName) => _notConfigured.TryRemove(collectionName, out _);

    public static IReadOnlyCollection<(string Email, string CollectionName, DateOnly SubmissionDeadline, string RecipientName)> Sent => _sent.ToArray();

    // The bag is static and therefore shared across every test class in the assembly (xUnit runs two at a time),
    // so assertions filter on a collection name unique to the test rather than counting the whole bag.
    public static void Clear()
    {
        _sent.Clear();
        _failing.Clear();
        _notConfigured.Clear();
    }
}