using System.Collections.Concurrent;
using Application.Common.Interfaces;
using LanguageExt;

namespace Tests.Common.Services;

// Test double for ICollectionWindowClosedLaEmailService — records the BLUM-8155 "window closed" notification
// instead of calling Notify, so integration tests can assert which LA users were told their organisation missed
// the deadline. Mirrors InMemoryCollectionClosedEmailService: FailFor makes sends for one named collection return
// a Left, and NotConfiguredFor makes them return the EmailNotConfiguredException Left specifically — the two are
// separate switches because CollectionWindowClosedLaNotifier treats them as opposites, exactly like
// CollectionClosureNotifier does.
public class InMemoryCollectionWindowClosedLaEmailService : ICollectionWindowClosedLaEmailService
{
    private static readonly ConcurrentBag<(string Email, string CollectionName, string RecipientName, DateOnly SubmissionDeadline)> _sent = new();

    // Keyed by collection name rather than a global on/off flag, same reasoning as InMemoryCollectionClosedEmailService:
    // two test classes run at a time, and a flag would fail every OTHER class's sends for as long as one held it.
    private static readonly ConcurrentDictionary<string, byte> _failing = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, byte> _notConfigured = new(StringComparer.Ordinal);

    public Task<Either<Exception, Unit>> SendWindowClosedAsync(
        string email,
        CollectionWindowClosedLaNotification notification,
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

        _sent.Add((email, notification.CollectionName, notification.RecipientName, notification.SubmissionDeadline));
        return Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }

    public static void FailFor(string collectionName) => _failing[collectionName] = 0;

    public static void StopFailingFor(string collectionName) => _failing.TryRemove(collectionName, out _);

    public static void NotConfiguredFor(string collectionName) => _notConfigured[collectionName] = 0;

    public static void ConfigureFor(string collectionName) => _notConfigured.TryRemove(collectionName, out _);

    public static IReadOnlyCollection<(string Email, string CollectionName, string RecipientName, DateOnly SubmissionDeadline)> Sent => _sent.ToArray();

    // The bag is static and therefore shared across every test class in the assembly (xUnit runs two at a time),
    // so assertions filter on a collection name unique to the test rather than counting the whole bag.
    public static void Clear()
    {
        _sent.Clear();
        _failing.Clear();
        _notConfigured.Clear();
    }
}