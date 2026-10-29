using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.MaturityAssessments.Exceptions;
using Domain.Collections;
using Domain.MaturityAssessments;
using Domain.Organisations;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Infrastructure.Persistence.Repositories;

public class MaturityAssessmentRepository(ApplicationDbContext dbContext)
    : RepositoryBase<MaturityAssessment, MaturityAssessmentId>(dbContext), IMaturityAssessmentRepository, IMaturityAssessmentQueries
{
    Task IMaturityAssessmentRepository.UpdateAsync(MaturityAssessment assessment, CancellationToken cancellationToken)
        => UpdateAsync(assessment, cancellationToken);

    async Task IMaturityAssessmentRepository.UpdateDetectingReferenceConflictAsync(
        MaturityAssessment assessment,
        CancellationToken cancellationToken)
    {
        // Bypasses RepositoryBase.UpdateAsync, which wraps every DbUpdateException into an opaque exception and would
        // hide the unique-violation the submit handler retries on (the SubmissionRepository precedent).
        if (Context.Entry(assessment).State == EntityState.Detached)
        {
            Context.MaturityAssessments.Update(assessment);
        }

        try
        {
            await Context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsReferenceNumberConflict(ex))
        {
            throw new MaturityAssessmentReferenceConflictException(assessment.Id);
        }
    }

    async Task<bool> IMaturityAssessmentRepository.TryAddAsync(MaturityAssessment assessment, CancellationToken cancellationToken)
    {
        await Context.MaturityAssessments.AddAsync(assessment, cancellationToken);
        try
        {
            await Context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (IsCollectionOrganisationConflict(ex))
        {
            // Detach the failed graph so the next SaveChanges on this context (the pre-fill insert never runs, but
            // anything else in the request might) does not re-flush it. Snapshot first: detaching a child removes it
            // from the parent's navigation collection, which is the collection being enumerated.
            foreach (var section in assessment.Sections.ToList())
            {
                Context.Entry(section).State = EntityState.Detached;
            }

            Context.Entry(assessment).State = EntityState.Detached;
            return false;
        }
    }

    private static bool IsCollectionOrganisationConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException postgres
        && postgres.SqlState == PostgresErrorCodes.UniqueViolation
        && postgres.ConstraintName is not null
        && postgres.ConstraintName.Contains("collection_id", StringComparison.Ordinal);

    private static bool IsReferenceNumberConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException postgres
        && postgres.SqlState == PostgresErrorCodes.UniqueViolation
        && postgres.ConstraintName is not null
        && postgres.ConstraintName.Contains("reference_number", StringComparison.Ordinal);

    public async Task<Option<MaturityAssessment>> GetByCollectionAndOrgAsync(
        CollectionId collectionId,
        OrganisationId organisationId,
        CancellationToken cancellationToken = default)
    {
        // Tracked load: command handlers mutate the aggregate (incl. its owned Sections) and rely on the change
        // tracker to diff children on save.
        var entity = await Context.MaturityAssessments
            .Include(a => a.Sections)
            .FirstOrDefaultAsync(
                a => a.CollectionId == collectionId && a.OrganisationId == organisationId,
                cancellationToken);

        return entity is null ? Option<MaturityAssessment>.None : Option<MaturityAssessment>.Some(entity);
    }

    public async Task<IReadOnlyList<MaturityAssessmentStatusRow>> GetStatusRowsForCollectionAsync(
        CollectionId collectionId,
        CancellationToken cancellationToken = default)
    {
        // Project the value-object id whole and unwrap .Value in memory (the SubmissionRepository precedent).
        var rows = await Context.MaturityAssessments
            .AsNoTracking()
            .Where(a => a.CollectionId == collectionId)
            .Select(a => new { a.OrganisationId, a.SubmittedAt })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new MaturityAssessmentStatusRow(r.OrganisationId.Value, r.SubmittedAt)).ToList();
    }

    public async Task<IReadOnlyList<OrganisationMaturityAssessmentRow>> GetForOrganisationAsync(
        OrganisationId organisationId,
        CancellationToken cancellationToken = default)
    {
        var rows = await Context.MaturityAssessments
            .AsNoTracking()
            .Where(a => a.OrganisationId == organisationId)
            .Select(a => new { a.CollectionId, a.SubmittedAt })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new OrganisationMaturityAssessmentRow(r.CollectionId.Value, r.SubmittedAt)).ToList();
    }

    public async Task<Option<PriorMaturityAssessmentRow>> GetLatestSubmittedBeforeAsync(
        OrganisationId organisationId,
        DateOnly beforeReportingPeriodEnd,
        CancellationToken cancellationToken = default)
    {
        // No collection-type predicate: CollectionType is a value object mapped through HasConversion, and only a
        // maturity-assessment collection can hold a maturity_assessments row at all. Soft-deleted collections are
        // excluded so a mistaken duplicate window never outranks the organisation's real one.
        var row = await (
            from assessment in Context.MaturityAssessments.AsNoTracking()
            join collection in Context.Collections.AsNoTracking() on assessment.CollectionId equals collection.Id
            where assessment.OrganisationId == organisationId
                && assessment.SubmittedAt != null
                && collection.DeletedAt == null
                && collection.ReportingPeriodEnd != null
                && collection.ReportingPeriodEnd < beforeReportingPeriodEnd
            orderby collection.ReportingPeriodEnd descending, assessment.SubmittedAt descending
            select new { assessment.Id, CollectionId = collection.Id, collection.FinancialYear, collection.ReportingPeriodEnd, assessment.SubmittedAt })
            .FirstOrDefaultAsync(cancellationToken);

        return row is null
            ? Option<PriorMaturityAssessmentRow>.None
            : new PriorMaturityAssessmentRow(row.Id.Value, row.CollectionId.Value, row.FinancialYear, row.ReportingPeriodEnd!.Value, row.SubmittedAt!.Value);
    }

    public async Task<Option<MaturityAssessmentDetailRow>> GetDetailForCollectionAndOrgAsync(
        CollectionId collectionId,
        OrganisationId organisationId,
        CancellationToken cancellationToken = default)
    {
        var row = await Context.MaturityAssessments
            .AsNoTracking()
            .Where(a => a.CollectionId == collectionId && a.OrganisationId == organisationId)
            .Select(a => new
            {
                a.Id,
                a.SubmittedAt,
                a.SubmittedBy,
                a.ReferenceNumber,
                a.SnapshotJson,
                Sections = a.Sections.Select(s => new { s.SectionCode, s.Status }).ToList(),
            })
            .FirstOrDefaultAsync(cancellationToken);

        return row is null
            ? Option<MaturityAssessmentDetailRow>.None
            : new MaturityAssessmentDetailRow(
                row.Id.Value,
                row.SubmittedAt,
                row.SubmittedBy?.Value,
                row.ReferenceNumber,
                row.SnapshotJson,
                row.Sections.Select(s => new MaturityAssessmentSectionRow(s.SectionCode, s.Status.ToString())).ToList());
    }

    public async Task<IReadOnlyList<MaturityAssessmentExportRow>> GetSubmittedForCollectionAsync(
        CollectionId collectionId,
        CancellationToken cancellationToken = default)
    {
        var rows = await Context.MaturityAssessments
            .AsNoTracking()
            .Where(a => a.CollectionId == collectionId && a.SubmittedAt != null)
            .Select(a => new { a.OrganisationId, a.SubmittedAt, a.SubmittedBy, a.ReferenceNumber, a.SnapshotJson })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new MaturityAssessmentExportRow(r.OrganisationId.Value, r.SubmittedAt!.Value, r.SubmittedBy?.Value, r.ReferenceNumber, r.SnapshotJson))
            .ToList();
    }

    public async Task<int> CountSubmittedForFinancialYearAsync(
        string financialYear,
        CancellationToken cancellationToken = default)
    {
        // Counted across every collection sharing the financial year — the reference prefix granularity — so the
        // next sequence is unique against all of them, not just one collection.
        return await (
            from assessment in Context.MaturityAssessments.AsNoTracking()
            join collection in Context.Collections.AsNoTracking() on assessment.CollectionId equals collection.Id
            where collection.FinancialYear == financialYear
                && assessment.SubmittedAt != null
            select assessment.Id)
            .CountAsync(cancellationToken);
    }

    public async Task<bool> HasAnyForOrganisationAsync(
        OrganisationId organisationId,
        CancellationToken cancellationToken = default)
    {
        return await Context.MaturityAssessments
            .AsNoTracking()
            .AnyAsync(a => a.OrganisationId == organisationId, cancellationToken);
    }
}