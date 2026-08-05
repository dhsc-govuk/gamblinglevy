using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.Submissions.Exceptions;
using Domain.Collections;
using Domain.Organisations;
using Domain.Submissions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Infrastructure.Persistence.Repositories;

public class SubmissionRepository(ApplicationDbContext dbContext)
    : RepositoryBase<Submission, SubmissionId>(dbContext), ISubmissionRepository, ISubmissionQueries, ISubmissionProbe
{
    Task ISubmissionRepository.UpdateAsync(Submission submission, CancellationToken cancellationToken)
        => UpdateAsync(submission, cancellationToken);

    async Task ISubmissionRepository.UpdateDetectingReferenceConflictAsync(
        Submission submission,
        CancellationToken cancellationToken)
    {
        // The submission is already change-tracked (mutated via Submit/AssignReference/AttachReportSnapshot), so we
        // persist it straight through the change tracker. We deliberately bypass RepositoryBase.UpdateAsync — it wraps
        // every DbUpdateException in a generic Exception, which would hide the unique-violation we need to detect and
        // surface as a retryable conflict instead of a 500.
        if (Context.Entry(submission).State == EntityState.Detached)
        {
            Context.Submissions.Update(submission);
        }

        try
        {
            await Context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsReferenceNumberConflict(ex))
        {
            throw new SubmissionReferenceConflictException(submission.Id);
        }
    }

    private static bool IsReferenceNumberConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException postgres
        && postgres.SqlState == PostgresErrorCodes.UniqueViolation
        && postgres.ConstraintName is not null
        && postgres.ConstraintName.Contains("reference_number", StringComparison.Ordinal);

    public async Task<Option<Submission>> GetByCollectionAndOrgAsync(
        CollectionId collectionId,
        OrganisationId organisationId,
        CancellationToken cancellationToken = default)
    {
        // Tracked load: command handlers mutate the aggregate (incl. its owned Sections) and rely on the change
        // tracker to diff children on save.
        var entity = await Context.Submissions
            .Include(s => s.Sections)
            .FirstOrDefaultAsync(
                s => s.CollectionId == collectionId && s.OrganisationId == organisationId,
                cancellationToken);

        return entity is null ? Option<Submission>.None : Option<Submission>.Some(entity);
    }

    public async Task<bool> HasAnyForOrganisationAsync(
        OrganisationId organisationId,
        CancellationToken cancellationToken = default)
    {
        return await Context.Submissions
            .AsNoTracking()
            .AnyAsync(s => s.OrganisationId == organisationId, cancellationToken);
    }

    public async Task<IReadOnlyList<(SubmissionId SubmissionId, int? Quarter)>> GetForOrgAndFinancialYearAsync(
        OrganisationId organisationId,
        string financialYear,
        CancellationToken cancellationToken = default)
    {
        var rows = await (
            from submission in Context.Submissions.AsNoTracking()
            join collection in Context.Collections.AsNoTracking() on submission.CollectionId equals collection.Id
            where submission.OrganisationId == organisationId && collection.FinancialYear == financialYear
            select new { submission.Id, collection.Quarter })
            .ToListAsync(cancellationToken);

        return rows.Select(r => (r.Id, r.Quarter)).ToList();
    }

    public async Task<int> CountSubmittedForPeriodAsync(
        string financialYear,
        int quarter,
        CancellationToken cancellationToken = default)
    {
        // Count finalised submissions across every collection sharing this financial year + quarter — the reference
        // prefix granularity — so the next sequence is unique against all of them, not just one collection.
        return await (
            from submission in Context.Submissions.AsNoTracking()
            join collection in Context.Collections.AsNoTracking() on submission.CollectionId equals collection.Id
            where collection.FinancialYear == financialYear
                && collection.Quarter == quarter
                && submission.SubmittedAt != null
            select submission.Id)
            .CountAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CollectionSubmissionRow>> GetSubmissionsForCollectionAsync(
        CollectionId collectionId,
        CancellationToken cancellationToken = default)
    {
        // At most ~160 rows per collection (153 LAs + a few VCSEs), so we load every submission for the collection and
        // let the controller service join against the assigned-org set in memory — the simple CollectionId equality
        // translates cleanly, unlike a Contains over a list of value-object ids. Project the value-object id whole and
        // unwrap .Value in memory (the GetForOrgAndFinancialYearAsync precedent).
        var rows = await Context.Submissions
            .AsNoTracking()
            .Where(s => s.CollectionId == collectionId)
            .Select(s => new { s.OrganisationId, s.SubmittedAt, s.ReferenceNumber, s.ReportSnapshotJson })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new CollectionSubmissionRow(r.OrganisationId.Value, r.SubmittedAt, r.ReferenceNumber, r.ReportSnapshotJson))
            .ToList();
    }

    public async Task<IReadOnlyList<CollectionSubmissionStatusRow>> GetStatusRowsForCollectionAsync(
        CollectionId collectionId,
        CancellationToken cancellationToken = default)
    {
        // Two columns only. The sibling above selects ReportSnapshotJson as well — a whole rendered report per
        // organisation — which the status screen would pull for every one of a window's ~160 organisations and then
        // discard. Project the value-object id whole and unwrap .Value in memory (the sibling's precedent).
        var rows = await Context.Submissions
            .AsNoTracking()
            .Where(s => s.CollectionId == collectionId)
            .Select(s => new { s.OrganisationId, s.SubmittedAt })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new CollectionSubmissionStatusRow(r.OrganisationId.Value, r.SubmittedAt))
            .ToList();
    }

    public async Task<Option<SubmissionDetailRow>> GetDetailForCollectionAndOrgAsync(
        CollectionId collectionId,
        OrganisationId organisationId,
        CancellationToken cancellationToken = default)
    {
        var row = await Context.Submissions
            .AsNoTracking()
            .Where(s => s.CollectionId == collectionId && s.OrganisationId == organisationId)
            .Select(s => new { s.Id, s.SubmittedAt, s.SubmittedBy, s.ReferenceNumber })
            .FirstOrDefaultAsync(cancellationToken);

        return row is null
            ? Option<SubmissionDetailRow>.None
            : new SubmissionDetailRow(row.Id.Value, row.SubmittedAt, row.SubmittedBy?.Value, row.ReferenceNumber);
    }

    public async Task<IReadOnlyList<OrganisationSubmissionRow>> GetForOrganisationAsync(
        OrganisationId organisationId,
        CancellationToken cancellationToken = default)
    {
        // One row per collection this org has started a submission for. Project the value-object id whole and unwrap
        // .Value in memory (the GetSubmissionsForCollectionAsync precedent) — the simple OrganisationId equality
        // translates cleanly.
        var rows = await Context.Submissions
            .AsNoTracking()
            .Where(s => s.OrganisationId == organisationId)
            .Select(s => new { s.CollectionId, s.SubmittedAt })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new OrganisationSubmissionRow(r.CollectionId.Value, r.SubmittedAt))
            .ToList();
    }
}