using System.Collections.Concurrent;
using Application.Common.Interfaces;
using LanguageExt;

namespace Tests.Common.Services;

// Test double for ITemporaryPasswordEmailService — records the notification instead of calling Notify, so
// integration tests can assert a temporary password was (or wasn't) emailed without ever seeing the password
// value itself leave the handler under test.
//
// One instance per test host, and what was sent is held on the instance, not in a static. Two classes clear this
// recorder before every test, and CI runs two classes at a time: while the bag was static, one class's Clear()
// could land between the other's request and its assertion and wipe the send it was about to look for. A test
// class has its own host, so it now has its own recorder — resolve it from the factory's services.
public class InMemoryTemporaryPasswordEmailService : ITemporaryPasswordEmailService
{
    private readonly ConcurrentBag<(string Email, string RecipientName)> _sent = new();

    private static readonly AsyncLocal<bool> FailingFlag = new();

    // Defaults to true (configured) so every existing test keeps exercising the send path unchanged; flip with
    // NotConfigure() to cover the guard the handlers now check before touching Entra at all.
    private static readonly AsyncLocal<bool?> ConfiguredFlag = new();

    public bool IsConfigured => ConfiguredFlag.Value ?? true;

    public Task<Either<Exception, Unit>> SendTemporaryPasswordAsync(
        string email,
        TemporaryPasswordNotification notification,
        CancellationToken cancellationToken)
    {
        if (FailingFlag.Value)
        {
            return Task.FromResult<Either<Exception, Unit>>(new Exception("Simulated Notify failure."));
        }

        _sent.Add((email, notification.RecipientName));
        return Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }

    public static void Fail() => FailingFlag.Value = true;

    public static void NotConfigure() => ConfiguredFlag.Value = false;

    public IReadOnlyCollection<(string Email, string RecipientName)> Sent => _sent.ToArray();

    public void Clear()
    {
        _sent.Clear();
        FailingFlag.Value = false;
        ConfiguredFlag.Value = null;
    }
}