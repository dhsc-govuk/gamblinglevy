using Application.AuditLogs;
using Application.AuditLogs.Dtos;
using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.Common.Validation;
using Domain.AuditLogs;
using Domain.Organisations;
using Domain.Users;
using LanguageExt;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class AuditLogRepository(ApplicationDbContext dbContext)
    : RepositoryBase<AuditLog, AuditLogId>(dbContext), IAuditLogRepository, IAuditLogQueries
{
    public Task<AuditLog> AppendAsync(AuditLog entry, CancellationToken cancellationToken = default)
    {
        return AddAsync(entry, cancellationToken);
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

        var pageRows = await query
            .OrderByDescending(x => x.OccurredAt)
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
            After: log.After);
    }

    private sealed record AuditLogLookups(IReadOnlyDictionary<Guid, string> Users, IReadOnlyDictionary<Guid, string> Organisations);
}