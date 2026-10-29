using System.Collections.Concurrent;
using Application.Common.Interfaces;
using LanguageExt;

namespace Tests.Common.Services;

// Test double for ICollectionClosedEmailService — records the closure notification instead of calling Notify, so
// integration tests can assert which DHSC admins were told a window had closed. Mirrors
// InMemorySubmissionConfirmationEmailService, with two additions: FailFor makes sends for one named collection
// return a Left, and NotConfiguredFor makes them return the EmailNotConfiguredException Left specifically. The two
// are separate switches because CollectionClosureNotifier treats them as opposites — a failed send is stamped as
// notified and never retried, an unconfigured template is left clear so the next sweep retries it — and a fake
// that could only produce one of them would let that distinction be deleted with every test still green.
public class InMemoryCollectionClosedEmailService : ICollectionClosedEmailService
{
    private static readonly ConcurrentBag<(string Email, string CollectionName, string RecipientName)> _sent = new();

    // Keyed by collection name rather than a global on/off flag. Two test classes run at a time, so a flag would
    // make every OTHER class's closure sends fail for as long as one test held it — dormant today only because
    // nothing else asserts on them.
    private static readonly ConcurrentDictionary<string, byte> _failing = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, byte> _notConfigured = new(StringComparer.Ordinal);

    public Task<Either<Exception, Unit>> SendClosureAsync(
        string email,
        CollectionClosureNotification notification,
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

        _sent.Add((email, notification.CollectionName, notification.RecipientName));
        return Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }

    public static void FailFor(string collectionName) => _failing[collectionName] = 0;

    public static void StopFailingFor(string collectionName) => _failing.TryRemove(collectionName, out _);

    public static void NotConfiguredFor(string collectionName) => _notConfigured[collectionName] = 0;

    public static void ConfigureFor(string collectionName) => _notConfigured.TryRemove(collectionName, out _);

    public static IReadOnlyCollection<(string Email, string CollectionName, string RecipientName)> Sent => _sent.ToArray();

    // The bag is static and therefore shared across every test class in the assembly (xUnit runs two at a time),
    // so assertions filter on a collection name unique to the test rather than counting the whole bag.
    public static void Clear()
    {
        _sent.Clear();
        _failing.Clear();
        _notConfigured.Clear();
    }
}