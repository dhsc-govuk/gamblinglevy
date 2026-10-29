using Api.Common;
using Application.AuditLogs;
using Application.AuditLogs.Dtos;
using Application.Common.Interfaces.Queries;
using Application.Common.Validation;
using Domain.ValueObjects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[Route("admin/audit-logs")]
[ApiController]
[Authorize(Roles = Role.Admin)]
public class AdminAuditLogsController(IAuditLogQueries auditLogQueries) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedAuditLogs<AuditLogListItemDto>>> GetPaged(
        [FromQuery] string? action,
        [FromQuery] Guid? actorUserId,
        [FromQuery] Guid? targetUserId,
        [FromQuery] Guid? organisationId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? sort = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = FieldLimits.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        QueryGuard.Paging(page, pageSize);

        // Bounded rather than allow-listed. The action catalogue gains entries with almost every ticket, so
        // pinning the list at the boundary would mean editing this controller each time — and a bookmarked
        // filter naming a retired action should show no results, not an error. The bound matches
        // audit_logs.action varchar(64), beyond which nothing could match anyway.
        QueryGuard.MaxLength(action, FieldLimits.AuditAction, nameof(action));

        // `sort` is allow-listed rather than bounded, unlike `action` above: it is a closed pair that the
        // repository switches on, so an unrecognised value would answer in the opposite order to the one
        // the caller asked for rather than return nothing.
        QueryGuard.AllowedValue(sort, AuditLogSort.IsSupported, nameof(sort), "Select a valid sort order.");

        var filter = new AuditLogFilter(action, actorUserId, targetUserId, organisationId, from, to, sort);
        var result = await auditLogQueries.GetPaged(filter, page, pageSize, cancellationToken);
        return result;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AuditLogDetailDto>> GetById(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var result = await auditLogQueries.GetById(id, cancellationToken);
        return result.Match<ActionResult<AuditLogDetailDto>>(
            dto => dto,
            NotFound());
    }
}