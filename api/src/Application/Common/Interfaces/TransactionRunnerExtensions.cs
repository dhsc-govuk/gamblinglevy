using LanguageExt;

namespace Application.Common.Interfaces;

public static class TransactionRunnerExtensions
{
    /// <summary>
    /// For work with no Left: committed when it returns, rolled back when it throws. The shape the shared
    /// collaborators (UserDeactivator, MfaFailureRecorder, IMfaSessionService) need, since they answer with a value
    /// rather than an Either.
    /// </summary>
    public static async Task<T> RunAsync<T>(this ITransactionRunner runner, Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken)
    {
        var result = await runner.RunAsync<Unit, T>(async token => await work(token), cancellationToken);
        return result.Match(value => value, _ => throw new InvalidOperationException("Unreachable: the work has no Left."));
    }

    /// <inheritdoc cref="RunAsync{T}(ITransactionRunner, Func{CancellationToken, Task{T}}, CancellationToken)"/>
    public static Task RunAsync(this ITransactionRunner runner, Func<CancellationToken, Task> work, CancellationToken cancellationToken)
    {
        return runner.RunAsync<Unit>(
            async token =>
            {
                await work(token);
                return Unit.Default;
            },
            cancellationToken);
    }
}