using Application.Common.Interfaces.Repositories;
using Domain.MaturityAssessments;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class MaturityAssessmentAnswerRepository(ApplicationDbContext dbContext)
    : RepositoryBase<MaturityAssessmentAnswer, MaturityAssessmentAnswerId>(dbContext), IMaturityAssessmentAnswerRepository
{
    public async Task<IReadOnlyList<MaturityAssessmentAnswer>> GetForAssessmentAsync(
        MaturityAssessmentId assessmentId,
        CancellationToken cancellationToken = default)
    {
        return await Context.MaturityAssessmentAnswers
            .AsNoTracking()
            .Where(a => a.AssessmentId == assessmentId)
            .ToListAsync(cancellationToken);
    }

    public async Task ReplaceForQuestionsAsync(
        MaturityAssessmentId assessmentId,
        IReadOnlyList<string> questionCodes,
        IReadOnlyList<MaturityAssessmentAnswer> answers,
        CancellationToken cancellationToken = default)
    {
        // ExecuteDelete auto-commits on its own, so without an explicit transaction the page's previous answers were
        // gone before the replacements were durable: a failed SaveChanges left the page empty, and two saves of the
        // same page could interleave delete/delete/insert/insert into a unique-index 500. One transaction, and the
        // unique index now only serialises the two rather than failing the second. A transaction the caller already
        // has open (SaveMaturityAnswers runs inside an ITransactionRunner, BLUM-8748) is joined rather than a second
        // begun — Npgsql does not nest — and the caller then owns the commit.
        await using var ownTransaction = Context.Database.CurrentTransaction is null
            ? await Context.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var codes = questionCodes.ToList();
        if (codes.Count > 0)
        {
            await Context.MaturityAssessmentAnswers
                .Where(a => a.AssessmentId == assessmentId && codes.Contains(a.QuestionCode))
                .ExecuteDeleteAsync(cancellationToken);
        }

        if (answers.Count > 0)
        {
            await Context.MaturityAssessmentAnswers.AddRangeAsync(answers, cancellationToken);
        }

        await Context.SaveChangesAsync(cancellationToken);
        if (ownTransaction is not null)
        {
            await ownTransaction.CommitAsync(cancellationToken);
        }
    }

    public async Task InsertAsync(IReadOnlyList<MaturityAssessmentAnswer> answers, CancellationToken cancellationToken = default)
    {
        if (answers.Count == 0)
        {
            return;
        }

        await Context.MaturityAssessmentAnswers.AddRangeAsync(answers, cancellationToken);
        try
        {
            await Context.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Left tracked as Added, these rows would be retried by the caller's next save and fail it too — the
            // AuditLogRepository.AppendAsync detach, for the same reason. StartMaturityAssessment inserts them in
            // the same transaction as its audit row (BLUM-8748): a failure rolls that attempt back, and it starts the
            // assessment again without them.
            foreach (var answer in answers)
            {
                Context.Entry(answer).State = EntityState.Detached;
            }

            throw;
        }
    }

    public async Task ReplaceRowsAsync(
        IReadOnlyList<MaturityAssessmentAnswerId> staleIds,
        IReadOnlyList<MaturityAssessmentAnswer> answers,
        CancellationToken cancellationToken = default)
    {
        // One transaction, as in ReplaceForQuestionsAsync: ExecuteDelete auto-commits on its own, so a refused
        // insert would otherwise leave the stale rows deleted and nothing in their place.
        await using var transaction = await Context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var ids = staleIds.ToList();
            if (ids.Count > 0)
            {
                var removed = await Context.MaturityAssessmentAnswers
                    .Where(a => ids.Contains(a.Id))
                    .ExecuteDeleteAsync(cancellationToken);

                // A row that has gone was replaced or cleared by a page save since the caller read it. The unique
                // index catches the first — the LA's new row refuses the insert below — but a CLEARED value leaves
                // nothing to collide with, so without this the insert would put back what the LA just removed.
                if (removed != ids.Count)
                {
                    throw new DbUpdateConcurrencyException(
                        $"Expected to replace {ids.Count} maturity-assessment answer row(s) but only {removed} still existed.");
                }
            }

            if (answers.Count > 0)
            {
                await Context.MaturityAssessmentAnswers.AddRangeAsync(answers, cancellationToken);
            }

            await Context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            // The refresh is best-effort and its caller carries on with the same context — the next assessment's
            // write, then the audit row. Left tracked as Added, these rows would fail those saves too (the
            // InsertAsync detach, for the same reason).
            foreach (var answer in answers)
            {
                Context.Entry(answer).State = EntityState.Detached;
            }

            throw;
        }
    }
}