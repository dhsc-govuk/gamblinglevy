using System.Collections.Concurrent;
using Application.Common.Interfaces;
using LanguageExt;

namespace Tests.Common.Services;

// Test double for ICollectionWindowOpenedEmailService (BLUM-7721). Records the whole notification rather than
// just the address, because AC3 is about what the email SAYS — the quarter, the reporting period's start and
// end, the deadline and the report name — and a recorder that only kept recipients would let that be asserted
// vacuously.
//
// Same two independent switches as InMemoryCollectionReminderEmailService, and for the same reason:
// CollectionReminderNotifier releases the window-open claim when Notify is unconfigured and keeps it when a send
// was attempted and failed, so a fake that could only produce one of those Lefts would let that distinction be
// deleted with every test still green.
public class InMemoryCollectionWindowOpenedEmailService : ICollectionWindowOpenedEmailService
{
    private static readonly ConcurrentBag<(string Email, string CollectionName, string PeriodLabel, DateOnly ReportingPeriodStart, DateOnly ReportingPeriodEnd, DateOnly SubmissionDeadline, string RecipientName)> _sent = new();

    // Keyed by collection name rather than a global on/off flag: two test classes run at a time, so a flag would
    // make every OTHER class's sends fail for as long as one test held it.
    private static readonly ConcurrentDictionary<string, byte> _failing = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, byte> _notConfigured = new(StringComparer.Ordinal);

    public Task<Either<Exception, Unit>> SendWindowOpenedAsync(
        string email,
        CollectionWindowOpenedNotification notification,
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

        _sent.Add((
            email,
            notification.CollectionName,
            notification.PeriodLabel,
            notification.ReportingPeriodStart,
            notification.ReportingPeriodEnd,
            notification.SubmissionDeadline,
            notification.RecipientName));

        return Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }

    public static void FailFor(string collectionName) => _failing[collectionName] = 0;

    public static void StopFailingFor(string collectionName) => _failing.TryRemove(collectionName, out _);

    public static void NotConfiguredFor(string collectionName) => _notConfigured[collectionName] = 0;

    public static void ConfigureFor(string collectionName) => _notConfigured.TryRemove(collectionName, out _);

    public static IReadOnlyCollection<(string Email, string CollectionName, string PeriodLabel, DateOnly ReportingPeriodStart, DateOnly ReportingPeriodEnd, DateOnly SubmissionDeadline, string RecipientName)> Sent => _sent.ToArray();

    // Resets the per-collection switches and NOTHING else. There is deliberately no Clear() emptying _sent: two
    // test classes write into this bag (CollectionWindowOpenedNotificationTests and the last two facts of
    // CollectionReminderNotificationTests), xUnit runs two classes at a time, and emptying it from either would
    // wipe the other's records mid-test. Assertions filter on a collection name unique to the test, which is the
    // isolation that actually holds; the switches below are keyed the same way and so cannot reach another class.
    public static void ResetSwitches()
    {
        _failing.Clear();
        _notConfigured.Clear();
    }
}