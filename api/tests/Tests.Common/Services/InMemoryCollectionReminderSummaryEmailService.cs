using System.Collections.Concurrent;
using Application.Common.Interfaces;
using LanguageExt;

namespace Tests.Common.Services;

// Test double for ICollectionReminderSummaryEmailService (BLUM-7571 AC5/AC6). Records the outstanding count as
// well as the recipient, because AC6's "confirms a reminder has been sent to outstanding local authorities" is
// carried by that number — including when it is zero.
//
// Same two independent switches as InMemoryCollectionReminderEmailService, and for the same reason.
public class InMemoryCollectionReminderSummaryEmailService : ICollectionReminderSummaryEmailService
{
    private static readonly ConcurrentBag<(string Email, string CollectionName, DateOnly SubmissionDeadline, int OutstandingCount, bool IsMaturityAssessment)> _sent = new();

    private static readonly ConcurrentDictionary<string, byte> _failing = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, byte> _notConfigured = new(StringComparer.Ordinal);

    public Task<Either<Exception, Unit>> SendReminderSummaryAsync(
        string email,
        CollectionReminderSummary summary,
        CancellationToken cancellationToken)
    {
        if (_notConfigured.ContainsKey(summary.CollectionName))
        {
            return Task.FromResult<Either<Exception, Unit>>(
                new EmailNotConfiguredException("Simulated missing Notify configuration."));
        }

        if (_failing.ContainsKey(summary.CollectionName))
        {
            return Task.FromResult<Either<Exception, Unit>>(new Exception("Simulated Notify failure."));
        }

        _sent.Add((email, summary.CollectionName, summary.SubmissionDeadline, summary.OutstandingOrganisationCount, summary.IsMaturityAssessment));
        return Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }

    public static void FailFor(string collectionName) => _failing[collectionName] = 0;

    public static void StopFailingFor(string collectionName) => _failing.TryRemove(collectionName, out _);

    public static void NotConfiguredFor(string collectionName) => _notConfigured[collectionName] = 0;

    public static void ConfigureFor(string collectionName) => _notConfigured.TryRemove(collectionName, out _);

    public static IReadOnlyCollection<(string Email, string CollectionName, DateOnly SubmissionDeadline, int OutstandingCount, bool IsMaturityAssessment)> Sent => _sent.ToArray();

    public static void Clear()
    {
        _sent.Clear();
        _failing.Clear();
        _notConfigured.Clear();
    }
}