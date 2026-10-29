using LanguageExt;

namespace Application.Common.Interfaces;

// Runs a unit of work in one database transaction: committed when the work returns Right, rolled back when it
// returns Left or throws. For a write that spans several repositories — each of which saves on its own — and must
// land all together or not at all (BLUM-8310's offline upload replaces a whole section's answers).
//
// It is also how an audited change keeps its audit row (BLUM-8748): the business writes and
// IAuditLogRepository.AppendAsync go inside one RunAsync, so the row commits with the change or neither does.
// AuditCoverageTests fails a handler that holds the audit repository without a runner, unless it is listed there
// with the reason it has to stay outside one.
//
// Repository calls inside the work join the transaction: SaveChanges, ExecuteDelete and ExecuteUpdate all run on
// the scoped context's connection.
//
// Runs compose. When a transaction is already open on the context, RunAsync joins it: the work runs inside the
// outer transaction and neither commits nor rolls back, because the outermost RunAsync owns both. That is what lets
// a shared collaborator (UserDeactivator, IMfaSessionService) be atomic on its own and still be part of a handler's
// transaction. The cost is that a Left from a joined run rolls nothing back by itself — the caller must propagate it
// so the outer run sees a Left too, or the outer commit keeps the inner work.
public interface ITransactionRunner
{
    Task<Either<TLeft, TRight>> RunAsync<TLeft, TRight>(
        Func<CancellationToken, Task<Either<TLeft, TRight>>> work,
        CancellationToken cancellationToken);

    /// <summary>Whether a transaction is open on the scoped context, i.e. whether a RunAsync now would join one.</summary>
    bool InTransaction { get; }
}