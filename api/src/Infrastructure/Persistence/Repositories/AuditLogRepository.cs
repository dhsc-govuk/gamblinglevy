using Application.AuditLogs;
using Application.AuditLogs.Dtos;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.Common.Validation;
using Domain.AuditLogs;
using Domain.Organisations;
using Domain.Users;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Persistence.Repositories;

public class AuditLogRepository(
    ApplicationDbContext dbContext,
    IDateTimeProvider dateTimeProvider,
    ILogger<AuditLogRepository> logger)
    : RepositoryBase<AuditLog, AuditLogId>(dbContext), IAuditLogRepository, IAuditLogQueries
{
    public async Task<AuditLog> AppendAsync(AuditLog entry, CancellationToken cancellationToken = default)
    {
        // The audit timestamp is stamped here, as the row is written, not by the caller (BLUM-8753): about seventy
        // call sites would otherwise each have to take a clock only to hand it straight back. AuditLog exposes no
        // setter for it on purpose, so it goes through the change tracker.
        Context.Entry(entry).Property(x => x.OccurredAt).CurrentValue = dateTimeProvider.GetUtcNow();

        try
        {
            return await AddAsync(entry, cancellationToken);
        }
        catch
        {
            // AddAsync leaves a failed insert tracked as Added in the request-scoped context. Several callers
            // swallow an audit failure on purpose (MfaDeviceMismatchRecorder, the rate-limit recorder) and the
            // request then carries on — so the next SaveChangesAsync, the handler's own, would retry the stuck
            // row and fail or roll back the user's actual change with it (BLUM-7848 review). Detaching makes a
            // lost audit row cost only the audit row.
            Context.Entry(entry).State = EntityState.Detached;
            throw;
        }
    }

    public async Task<bool> TryAppendAsync(AuditLog entry)
    {
        // A failed statement aborts the whole PostgreSQL transaction (25P02), so swallowing it here would leave the
        // caller's transaction unable to commit anything — "best effort" cannot exist inside one. Inside a
        // transaction the row belongs to the change and goes through AppendAsync (BLUM-8748).
        if (Context.Database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException(
                "TryAppendAsync cannot run inside a transaction; use AppendAsync so the audit row commits or rolls back with the change.");
        }

        try
        {
            await AppendAsync(entry, CancellationToken.None);
            return true;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Audit row {Action} for {TargetType} {TargetId} could not be written; the change it records was kept",
                entry.Action,
                entry.TargetType,
                entry.TargetId);
            return false;
        }
    }

    public async Task<AuditLogDeletionResult> DeleteOccurredBeforeAsync(DateTime cutoff, int batchSize, CancellationToken cancellationToken = default)
    {
        // Two statements because ExecuteDelete does not accept Take(), as in MfaSessionRepository.DeleteEndedSessionsAsync.
        // The select is an index range scan on ix_audit_logs_occurred_at, and the bound keeps a first sweep facing
        // years of backlog from holding locks on the table every audited write appends to.
        var doomed = await Context.AuditLogs
            .Where(x => x.OccurredAt < cutoff)
            .OrderBy(x => x.OccurredAt)
            .Take(batchSize)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        if (doomed.Count == 0)
        {
            return new AuditLogDeletionResult(Found: 0, Deleted: 0);
        }

        var deleted = await Context.AuditLogs
            .Where(x => doomed.Contains(x.Id))
            .ExecuteDeleteAsync(cancellationToken);

        // Deleted can fall short of Found when another instance sweeps the same rows; the caller loops on Found.
        return new AuditLogDeletionResult(Found: doomed.Count, Deleted: deleted);
    }

    public async Task<PagedAuditLogs<AuditLogListItemDto>> GetPaged(AuditLogFilter filter, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = Context.AuditLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrEmpty(filter.Action))
        {
            query = query.Where(x => x.Action == filter.Action);
        }

        if (filter.ActorUserId is { } actorId)
        {
            var actor = new UserId(actorId);
            query = query.Where(x => x.ActorUserId == actor);
        }

        if (filter.TargetUserId is { } targetId)
        {
            query = query.Where(x => x.TargetType == AuditTargetType.User && x.TargetId == targetId);
        }

        if (filter.OrganisationId is { } orgId)
        {
            var org = new OrganisationId(orgId);
            query = query.Where(x => x.OrganisationId == org);
        }

        if (filter.FromUtc is { } from)
        {
            query = query.Where(x => x.OccurredAt >= from);
        }

        if (filter.ToUtc is { } to)
        {
            query = query.Where(x => x.OccurredAt <= to);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        // Both callers now reject an out-of-range page with a 400 before reaching this method (QueryGuard.Paging),
        // so this is a backstop rather than the enforcement point. It reads the same constants so the two cannot
        // drift, and no longer silently answers 200 OK for a page the caller never asked for. The Skip
        // multiplication is computed in an int, which is why the page number is bounded and not merely
        // floored (BLUM-7240).
        var safePage = Math.Clamp(page, 1, FieldLimits.MaxPage);
        var safePageSize = Math.Clamp(pageSize, FieldLimits.MinPageSize, FieldLimits.MaxPageSize);

        // Newest is the default for an absent or null sort — the audit view has always opened on the most
        // recent event, and the deleted-user views that reuse this endpoint depend on it.
        //
        // Id breaks the tie in both directions. occurred_at is not unique — AppendAsync stamps each row from
        // the application clock, whose resolution is coarser than the gap between two appends in one request,
        // and a row added outside it takes the column's default, a single timezone('utc', now()) per
        // statement — so ordering on it alone leaves rows with an identical instant in whatever order the
        // plan happens to produce. Under Skip/Take that is not merely cosmetic: the same row can appear on
        // two pages while another is never returned at all.
        var ordered = filter.Sort == AuditLogSort.Oldest
            ? query.OrderBy(x => x.OccurredAt).ThenBy(x => x.Id)
            : query.OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Id);

        var pageRows = await ordered
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .ToListAsync(cancellationToken);

        var enriched = await EnrichAsync(pageRows, cancellationToken);

        return new PagedAuditLogs<AuditLogListItemDto>(enriched, totalCount, safePage, safePageSize);
    }

    public async Task<Option<AuditLogDetailDto>> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var log = await Context.AuditLogs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == new AuditLogId(id), cancellationToken);
        if (log is null)
        {
            return Option<AuditLogDetailDto>.None;
        }

        var lookups = await ResolveLookupsAsync(new[] { log }, cancellationToken);
        return ToDetailDto(log, lookups);
    }

    private async Task<IReadOnlyList<AuditLogListItemDto>> EnrichAsync(IReadOnlyList<AuditLog> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return Array.Empty<AuditLogListItemDto>();
        }

        var lookups = await ResolveLookupsAsync(rows, cancellationToken);
        return rows.Select(log => ToListItemDto(log, lookups)).ToList();
    }

    private async Task<AuditLogLookups> ResolveLookupsAsync(IReadOnlyList<AuditLog> rows, CancellationToken cancellationToken)
    {
        var actorIds = rows.Select(x => x.ActorUserId).Distinct().ToList();
        var targetUserIds = rows
            .Where(x => x.TargetType == AuditTargetType.User)
            .Select(x => new UserId(x.TargetId))
            .Distinct()
            .ToList();
        var allUserIds = actorIds.Concat(targetUserIds).Distinct().ToList();
        var orgIds = rows
            .Where(x => x.OrganisationId is not null)
            .Select(x => x.OrganisationId!)
            .Distinct()
            .ToList();

        var users = allUserIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await Context.Users.AsNoTracking()
                .Where(u => allUserIds.Contains(u.Id))
                .Select(u => new { u.Id, u.Name.FirstName, u.Name.LastName })
                .ToDictionaryAsync(u => u.Id.Value, u => $"{u.FirstName} {u.LastName}".Trim(), cancellationToken);

        var orgs = orgIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await Context.Organisations.AsNoTracking()
                .Where(o => orgIds.Contains(o.Id))
                .Select(o => new { o.Id, o.Name })
                .ToDictionaryAsync(o => o.Id.Value, o => o.Name, cancellationToken);

        return new AuditLogLookups(users, orgs);
    }

    private static AuditLogListItemDto ToListItemDto(AuditLog log, AuditLogLookups lookups)
    {
        return new AuditLogListItemDto(
            Id: log.Id.Value,
            OccurredAt: log.OccurredAt,
            Action: log.Action,
            ActorUserId: log.ActorUserId.Value,
            ActorDisplayName: lookups.Users.GetValueOrDefault(log.ActorUserId.Value),
            TargetType: log.TargetType,
            TargetId: log.TargetId,
            TargetDisplayName: log.TargetType == AuditTargetType.User ? lookups.Users.GetValueOrDefault(log.TargetId) : null,
            OrganisationId: log.OrganisationId?.Value,
            OrganisationName: log.OrganisationId is null ? null : lookups.Organisations.GetValueOrDefault(log.OrganisationId.Value));
    }

    private static AuditLogDetailDto ToDetailDto(AuditLog log, AuditLogLookups lookups)
    {
        return new AuditLogDetailDto(
            Id: log.Id.Value,
            OccurredAt: log.OccurredAt,
            Action: log.Action,
            ActorUserId: log.ActorUserId.Value,
            ActorDisplayName: lookups.Users.GetValueOrDefault(log.ActorUserId.Value),
            TargetType: log.TargetType,
            TargetId: log.TargetId,
            TargetDisplayName: log.TargetType == AuditTargetType.User ? lookups.Users.GetValueOrDefault(log.TargetId) : null,
            OrganisationId: log.OrganisationId?.Value,
            OrganisationName: log.OrganisationId is null ? null : lookups.Organisations.GetValueOrDefault(log.OrganisationId.Value),
            Before: log.Before,
            After: log.After,
            PayloadVersion: log.PayloadVersion);
    }

    private sealed record AuditLogLookups(IReadOnlyDictionary<Guid, string> Users, IReadOnlyDictionary<Guid, string> Organisations);
}