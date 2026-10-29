using System.Collections.Concurrent;
using Application.Common.Interfaces;
using Domain.ValueObjects;
using LanguageExt;

namespace Tests.Common.Services;

public class InMemoryInvitationEmailService : IInvitationEmailService
{
    private static readonly ConcurrentBag<(string Email, string Name, string Role)> _sent = new();

    // Keyed by address, like every assertion against this fake, because the bag is shared by classes running in
    // parallel: making one test's send fail must not fail another's.
    private static readonly ConcurrentDictionary<string, bool> _failingFor = new(StringComparer.OrdinalIgnoreCase);

    public Task<Either<Exception, Unit>> SendInvitationAsync(string email, Name name, UserRole role, CancellationToken cancellationToken)
    {
        if (_failingFor.ContainsKey(email))
        {
            return Task.FromResult<Either<Exception, Unit>>(new Exception($"Simulated invitation failure for {email}"));
        }

        _sent.Add((email, name.ToString(), role.ToString()));
        return Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }

    public static IReadOnlyCollection<(string Email, string Name, string Role)> Sent => _sent.ToArray();

    public static void Clear() => _sent.Clear();

    /// <summary>Makes every send to this address answer Left, as the provider does on failure, until <see cref="StopFailingFor"/>.</summary>
    public static void FailFor(string email) => _failingFor[email] = true;

    public static void StopFailingFor(string email) => _failingFor.TryRemove(email, out _);
}