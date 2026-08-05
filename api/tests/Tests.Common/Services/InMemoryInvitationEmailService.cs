using System.Collections.Concurrent;
using Application.Common.Interfaces;
using Domain.ValueObjects;
using LanguageExt;

namespace Tests.Common.Services;

public class InMemoryInvitationEmailService : IInvitationEmailService
{
    private static readonly ConcurrentBag<(string Email, string Name)> _sent = new();

    public Task<Either<Exception, Unit>> SendInvitationAsync(string email, Name name, CancellationToken cancellationToken)
    {
        _sent.Add((email, name.ToString()));
        return Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }

    public static IReadOnlyCollection<(string Email, string Name)> Sent => _sent.ToArray();

    public static void Clear() => _sent.Clear();
}