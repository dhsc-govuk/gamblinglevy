using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.Submissions.Exceptions;
using Domain.Activities;
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
        // collection.DeletedAt == null matters here: an org can otherwise end up with two submissions sharing the
        // same quarter — a mistaken/duplicate collection that was later soft-deleted, with the org's real report
        // living against a recreated one for the same quarter — and every caller of this query (the prior-quarter
        // accumulation above, and SpendReportTotals' quarter-bounded total) sums by quarter number, not by
        // submission identity, so the deleted collection's spend would otherwise double-count on top of the real
        // submission for that same quarter.
        var rows = await (
            from submission in Context.Submissions.AsNoTracking()
            join collection in Context.Collections.AsNoTracking() on submission.CollectionId equals collection.Id
            where submission.OrganisationId == organisationId
                && collection.FinancialYear == financialYear
                && collection.DeletedAt == null
            select new { submission.Id, collection.Quarter })
            .ToListAsync(cancellationToken);

        return rows.Select(r => (r.Id, r.Quarter)).ToList();
    }

    public async Task<IReadOnlyList<PriorActivityAnswerRow>> GetPriorSubmittedActivityAnswersAsync(
        OrganisationId organisationId,
        ActivityId activityId,
        DateOnly beforeReportingPeriodEnd,
        CancellationToken cancellationToken = default)
    {
        // "Earlier reporting period" is the whole filter: it excludes the current collection and any re-issue
        // covering the same period, so no separate collection-id exclusion is needed. ReportingPeriodEnd is
        // derived from FinancialYear + Quarter for every published collection (ReportingPeriod.Derive), so it is
        // always present here; a draft has no submissions to find anyway.
        //
        // collection.DeletedAt == null for the reason the financial-year query above spells out: a mistaken
        // duplicate window that was later soft-deleted must not outrank the org's real report for that period.
        //
        // No collection-type filter: Type is a value object mapped through HasConversion and no repository
        // compares one in SQL, and the join to submission_activity_answers already excludes every non-quarterly
        // submission — only the quarterly report records activity answers.
        var rows = await (
            from answer in Context.SubmissionActivityAnswers.AsNoTracking()
            join submission in Context.Submissions.AsNoTracking() on answer.SubmissionId equals submission.Id
            join collection in Context.Collections.AsNoTracking() on submission.CollectionId equals collection.Id
            where answer.ActivityId == activityId
                && submission.OrganisationId == organisationId
                && submission.SubmittedAt != null
                && collection.DeletedAt == null
                && collection.ReportingPeriodEnd != null
                && collection.ReportingPeriodEnd < beforeReportingPeriodEnd
            orderby collection.ReportingPeriodEnd descending, submission.SubmittedAt descending
            select new { submission.Id, answer.QuestionCode, answer.Value })
            .ToListAsync(cancellationToken);

        // Project the value-object id whole and unwrap .Value in memory (the GetSubmissionsForCollectionAsync
        // precedent). The id rides along so the caller can take one report's answer set whole.
        return rows.Select(r => new PriorActivityAnswerRow(r.Id.Value, r.QuestionCode, r.Value)).ToList();
    }

    public async Task<IReadOnlyList<OrganisationActivityAnswerRow>> GetLatestSubmittedActivityAnswersForOrganisationAsync(
        OrganisationId organisationId,
        DateOnly onOrBeforeReportingPeriodEnd,
        CancellationToken cancellationToken = default)
    {
        // The organisation-wide twin of GetPriorSubmittedActivityAnswersAsync above, for the maturity-assessment
        // pre-fill: every activity at once, periods ending on or before the assessment's own period end (the
        // assessment covers the year the reports were filed in). Same finalised-only / non-deleted / no-type
        // reasoning as its sibling.
        var rows = await (
            from answer in Context.SubmissionActivityAnswers.AsNoTracking()
            join submission in Context.Submissions.AsNoTracking() on answer.SubmissionId equals submission.Id
            join collection in Context.Collections.AsNoTracking() on submission.CollectionId equals collection.Id
            where submission.OrganisationId == organisationId
                && submission.SubmittedAt != null
                && collection.DeletedAt == null
                && collection.ReportingPeriodEnd != null
                && collection.ReportingPeriodEnd <= onOrBeforeReportingPeriodEnd
            orderby answer.ActivityId, collection.ReportingPeriodEnd descending, submission.SubmittedAt descending
            select new { answer.ActivityId, submission.Id, collection.ReportingPeriodEnd, submission.SubmittedAt, answer.QuestionCode, answer.Value })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new OrganisationActivityAnswerRow(r.ActivityId.Value, r.Id.Value, r.ReportingPeriodEnd!.Value, r.SubmittedAt!.Value, r.QuestionCode, r.Value))
            .ToList();
    }

    public async Task<IReadOnlyList<SubmittedActivityAnswerRow>> GetSubmittedAnswersForActivityAsync(
        OrganisationId organisationId,
        ActivityId activityId,
        CancellationToken cancellationToken = default)
    {
        // Same finalised-only / non-deleted / no-type reasoning as GetPriorSubmittedActivityAnswersAsync above.
        var rows = await (
            from answer in Context.SubmissionActivityAnswers.AsNoTracking()
            join submission in Context.Submissions.AsNoTracking() on answer.SubmissionId equals submission.Id
            join collection in Context.Collections.AsNoTracking() on submission.CollectionId equals collection.Id
            where answer.ActivityId == activityId
                && submission.OrganisationId == organisationId
                && submission.SubmittedAt != null
                && collection.DeletedAt == null
            orderby collection.ReportingPeriodEnd, submission.SubmittedAt
            select new
            {
                submission.Id,
                collection.Name,
                collection.FinancialYear,
                collection.Quarter,
                collection.ReportingPeriodEnd,
                submission.SubmittedAt,
                submission.ReferenceNumber,
                answer.QuestionCode,
                answer.Value,
            })
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return [];
        }

        // Status is a value object mapped through HasConversion, so "was this activity's report Completed" is decided
        // in memory — one row per report the activity took part in, so the read stays small.
        var reports = await Context.SubmissionActivityReports
            .AsNoTracking()
            .Where(r => r.ActivityId == activityId)
            .Select(r => new { r.SubmissionId, r.Status })
            .ToListAsync(cancellationToken);
        var completed = reports.Where(r => r.Status.IsCompleted).Select(r => r.SubmissionId.Value).ToHashSet();

        return rows
            .Where(r => completed.Contains(r.Id.Value))
            .Select(r => new SubmittedActivityAnswerRow(
                new SubmittedReportRef(r.Id.Value, r.Name, r.FinancialYear, r.Quarter, r.ReportingPeriodEnd, r.SubmittedAt!.Value, r.ReferenceNumber),
                r.QuestionCode,
                r.Value))
            .ToList();
    }

    public async Task<IReadOnlyList<SubmittedActivitySpendRow>> GetSubmittedSpendForActivityAsync(
        OrganisationId organisationId,
        ActivityId activityId,
        CancellationToken cancellationToken = default)
    {
        var rows = await (
            from entry in Context.SubmissionSpendEntries.AsNoTracking()
            join submission in Context.Submissions.AsNoTracking() on entry.SubmissionId equals submission.Id
            join collection in Context.Collections.AsNoTracking() on submission.CollectionId equals collection.Id
            where entry.ActivityId == activityId
                && submission.OrganisationId == organisationId
                && submission.SubmittedAt != null
                && collection.DeletedAt == null
            orderby collection.ReportingPeriodEnd, submission.SubmittedAt, entry.Quarter
            select new
            {
                submission.Id,
                collection.Name,
                collection.FinancialYear,
                collection.Quarter,
                collection.ReportingPeriodEnd,
                submission.SubmittedAt,
                submission.ReferenceNumber,
                entry.Kind,
                EntryQuarter = entry.Quarter,
                entry.Amount,
            })
            .ToListAsync(cancellationToken);

        // Kind is a value object mapped through HasConversion; every spend repository reads it in memory.
        return rows
            .Select(r => new SubmittedActivitySpendRow(
                new SubmittedReportRef(r.Id.Value, r.Name, r.FinancialYear, r.Quarter, r.ReportingPeriodEnd, r.SubmittedAt!.Value, r.ReferenceNumber),
                r.Kind.IsForecast,
                r.EntryQuarter,
                r.Amount))
            .ToList();
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

    public Task<bool> AnyForCollectionAsync(CollectionId collectionId, CancellationToken cancellationToken = default) =>
        Context.Submissions.AsNoTracking().AnyAsync(s => s.CollectionId == collectionId, cancellationToken);

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