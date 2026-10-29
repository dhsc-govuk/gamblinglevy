using System.Collections.Concurrent;
using Application.Common.Interfaces;
using LanguageExt;

namespace Tests.Common.Services;

// Test double for ICollectionWindowOpenedSummaryEmailService (BLUM-7721 AC2/AC4). Records the assigned-
// organisation count as well as the recipient and the period facts, because that number is the only thing that
// makes the DHSC copy say more than the provider email does — including when it is zero.
//
// Same two independent switches as its sibling, and for the same reason.
public class InMemoryCollectionWindowOpenedSummaryEmailService : ICollectionWindowOpenedSummaryEmailService
{
    private static readonly ConcurrentBag<(string Email, string CollectionName, string RecipientName, string PeriodLabel, DateOnly ReportingPeriodStart, DateOnly ReportingPeriodEnd, DateOnly SubmissionDeadline, int AssignedCount, bool IsMaturityAssessment)> _sent = new();

    private static readonly ConcurrentDictionary<string, byte> _failing = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, byte> _notConfigured = new(StringComparer.Ordinal);

    public Task<Either<Exception, Unit>> SendWindowOpenedSummaryAsync(
        string email,
        CollectionWindowOpenedSummary summary,
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

        _sent.Add((
            email,
            summary.CollectionName,
            summary.RecipientName,
            summary.PeriodLabel,
            summary.ReportingPeriodStart,
            summary.ReportingPeriodEnd,
            summary.SubmissionDeadline,
            summary.AssignedOrganisationCount,
            summary.IsMaturityAssessment));

        return Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }

    public static void FailFor(string collectionName) => _failing[collectionName] = 0;

    public static void StopFailingFor(string collectionName) => _failing.TryRemove(collectionName, out _);

    public static void NotConfiguredFor(string collectionName) => _notConfigured[collectionName] = 0;

    public static void ConfigureFor(string collectionName) => _notConfigured.TryRemove(collectionName, out _);

    public static IReadOnlyCollection<(string Email, string CollectionName, string RecipientName, string PeriodLabel, DateOnly ReportingPeriodStart, DateOnly ReportingPeriodEnd, DateOnly SubmissionDeadline, int AssignedCount, bool IsMaturityAssessment)> Sent => _sent.ToArray();

    // Switches only, and no Clear() emptying _sent — see InMemoryCollectionWindowOpenedEmailService for why.
    public static void ResetSwitches()
    {
        _failing.Clear();
        _notConfigured.Clear();
    }
}