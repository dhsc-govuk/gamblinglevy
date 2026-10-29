using System.Collections.Concurrent;
using Application.Common.Interfaces;
using LanguageExt;

namespace Tests.Common.Services;

// Test double for IMaturityAssessmentConfirmationEmailService — records the confirmation instead of calling Notify,
// so integration tests can assert the submit flow sent it to the right recipient. Mirrors
// InMemorySubmissionConfirmationEmailService.
public class InMemoryMaturityAssessmentConfirmationEmailService : IMaturityAssessmentConfirmationEmailService
{
    private static readonly ConcurrentBag<(string Email, string ReferenceNumber)> _sent = new();

    public Task<Either<Exception, Unit>> SendConfirmationAsync(
        string email,
        MaturityAssessmentConfirmation confirmation,
        CancellationToken cancellationToken)
    {
        _sent.Add((email, confirmation.ReferenceNumber));
        return Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }

    public static IReadOnlyCollection<(string Email, string ReferenceNumber)> Sent => _sent.ToArray();

    public static void Clear() => _sent.Clear();
}