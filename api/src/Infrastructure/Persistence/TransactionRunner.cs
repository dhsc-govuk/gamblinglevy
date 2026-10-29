using Application.Common.Interfaces;
using LanguageExt;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

// See ITransactionRunner. On a rollback the change tracker is cleared as well: the work usually mutates tracked
// entities (the submission's section statuses), and leaving them tracked would let a later SaveChanges in the same
// request quietly write back what the rollback just undid.
public class TransactionRunner(ApplicationDbContext dbContext) : ITransactionRunner
{
    public bool InTransaction => dbContext.Database.CurrentTransaction is not null;

    public async Task<Either<TLeft, TRight>> RunAsync<TLeft, TRight>(
        Func<CancellationToken, Task<Either<TLeft, TRight>>> work,
        CancellationToken cancellationToken)
    {
        if (InTransaction)
        {
            // Joined: the outermost run commits or rolls back, so this one only does the work (BLUM-8748).
            return await work(cancellationToken);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        Either<TLeft, TRight> result;
        try
        {
            result = await work(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            dbContext.ChangeTracker.Clear();
            throw;
        }

        if (result.IsRight)
        {
            try
            {
                // A statement that failed inside the work and was caught there has aborted the transaction (25P02),
                // and PostgreSQL answers the COMMIT that follows with a silent ROLLBACK — the request would report a
                // change that was thrown away. Any statement fails on an aborted transaction, so this cheap probe
                // turns that case into a rollback the caller hears about (BLUM-8748).
                await dbContext.Database.ExecuteSqlRawAsync("SELECT 1", cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                dbContext.ChangeTracker.Clear();
                throw;
            }

            await transaction.CommitAsync(cancellationToken);
        }
        else
        {
            await transaction.RollbackAsync(CancellationToken.None);
            dbContext.ChangeTracker.Clear();
        }

        return result;
    }
}