using System.Collections.Concurrent;
using Application.Common.Interfaces;
using LanguageExt;

namespace Tests.Common.Services;

public class InMemoryNewSignInEmailService : INewSignInEmailService
{
    private static readonly ConcurrentBag<(string Email, NewSignInNotification Notification)> _sent = new();

    public Task<Either<Exception, Unit>> SendNewSignInAsync(
        string email,
        NewSignInNotification notification,
        CancellationToken cancellationToken)
    {
        _sent.Add((email, notification));
        return Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }

    public static IReadOnlyCollection<(string Email, NewSignInNotification Notification)> Sent => _sent.ToArray();

    public static void Clear() => _sent.Clear();
}