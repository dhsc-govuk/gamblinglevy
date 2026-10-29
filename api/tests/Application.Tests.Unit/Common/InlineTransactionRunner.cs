using Application.Common.Interfaces;
using LanguageExt;

namespace Application.Tests.Unit.Common;

/// <summary>
/// Runs the work straight through, with no database. <see cref="InTransaction"/> is settable so a test can stand a
/// collaborator inside a caller's transaction (BLUM-8748) and check what it leaves to that caller.
/// </summary>
public sealed class InlineTransactionRunner(List<string>? calls = null) : ITransactionRunner
{
    public bool InTransaction { get; set; }

    public async Task<Either<TLeft, TRight>> RunAsync<TLeft, TRight>(
        Func<CancellationToken, Task<Either<TLeft, TRight>>> work,
        CancellationToken cancellationToken)
    {
        if (InTransaction)
        {
            return await work(cancellationToken);
        }

        InTransaction = true;
        try
        {
            var result = await work(cancellationToken);
            calls?.Add(result.IsRight ? "commit" : "rollback");
            return result;
        }
        catch
        {
            calls?.Add("rollback");
            throw;
        }
        finally
        {
            InTransaction = false;
        }
    }
}