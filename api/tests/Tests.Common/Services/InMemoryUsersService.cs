using Application.Users.Interfaces;
using Domain.ValueObjects;
using LanguageExt;

namespace Tests.Common.Services;

public class InMemoryUsersService : IUsersService
{
    public Task<Option<string>> FindById(string userId, CancellationToken cancellationToken)
    {
        return Task.FromResult<Option<string>>(Guid.NewGuid().ToString());
    }

    public async Task<Either<Exception, Option<string>>> FindByEmail(string email, CancellationToken cancellationToken)
    {
        await DelayIfAskedAsync(cancellationToken);
        if (TestGraphContext.FindByEmailFails)
        {
            return new InvalidOperationException("Graph unavailable");
        }

        return Option<string>.Some(Guid.NewGuid().ToString());
    }

    public async Task<Either<Exception, string>> Add(Name userName, string email, CancellationToken cancellationToken)
    {
        await DelayIfAskedAsync(cancellationToken);
        return Guid.NewGuid().ToString();
    }

    // Stands in for the round trip to Microsoft Graph. Off unless a test asks for it — see
    // TestGraphContext.LookupDelay for why anything here needs to take time at all, and
    // TestGraphContext.HoldCallsUntilInFlight for the gate.
    private static async Task DelayIfAskedAsync(CancellationToken cancellationToken)
    {
        if (TestGraphContext.CallGate is { } gate)
        {
            await gate.EnterAsync(cancellationToken);
        }

        var delay = TestGraphContext.LookupDelay;
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, cancellationToken);
        }
    }

    public Task<Either<Exception, Unit>> Update(string id, Name userName, string email, bool passwordLoginEnabled, CancellationToken cancellationToken)
    {
        return Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }

    public Task<Either<Exception, Option<string>>> EnablePasswordLogin(string id, Name userName, string email, string temporaryPassword, CancellationToken cancellationToken)
    {
        return Task.FromResult<Either<Exception, Option<string>>>(
            TestGraphContext.EnablePasswordLoginFails
                ? new InvalidOperationException("Graph unavailable")
                : TestGraphContext.RecreatedSubId is { } recreated
                    ? Option<string>.Some(recreated.ToString())
                    : Option<string>.None);
    }

    public Task<Either<Exception, Unit>> ResetPassword(string id, string email, string temporaryPassword, CancellationToken cancellationToken)
    {
        return Task.FromResult<Either<Exception, Unit>>(
            TestGraphContext.ResetPasswordFails
                ? new InvalidOperationException("Graph unavailable")
                : Unit.Default);
    }

    public Task<Either<Exception, Unit>> Disable(string id, CancellationToken cancellationToken)
    {
        return Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }

    public Task<Either<Exception, Unit>> Enable(string id, CancellationToken cancellationToken)
    {
        return Task.FromResult<Either<Exception, Unit>>(
            TestGraphContext.EnableFails
                ? new InvalidOperationException("Graph unavailable")
                : Unit.Default);
    }

    public Task<Either<Exception, Unit>> HardDelete(string id, CancellationToken cancellationToken)
    {
        return Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }
}