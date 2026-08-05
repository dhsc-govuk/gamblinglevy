using Api.Common;
using Application.AuditLogs;
using Application.AuditLogs.Dtos;
using Application.Common.Interfaces.Queries;
using Application.Common.Permissions;
using Application.Common.Validation;
using Domain.ValueObjects;
using LanguageExt.UnsafeValueAccess;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[Route("organisation-admin/audit-logs")]
[ApiController]
// Read-only for every LA role, Lead included (BLUM-7038). Scoping to the caller's own organisation is
// enforced by PermissionsService/the audit-log queries, not by this attribute.
[Authorize(Roles = Role.AnyOrganisationMember)]
public class OrganisationAuditLogsController(
    PermissionsService permissionsService,
    IAuditLogQueries auditLogQueries) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedAuditLogs<AuditLogListItemDto>>> GetPaged(
        [FromQuery] string? action,
        [FromQuery] Guid? actorUserId,
        [FromQuery] Guid? targetUserId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = FieldLimits.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        // See AdminAuditLogsController for why `action` is bounded rather than allow-listed.
        QueryGuard.Paging(page, pageSize);
        QueryGuard.MaxLength(action, FieldLimits.AuditAction, nameof(action));

        var permissions = permissionsService.GetOrganisationPermissions();
        return await permissions.MatchAsync<ActionResult<PagedAuditLogs<AuditLogListItemDto>>>(
            async p =>
            {
                if (p.OrganisationId.IsNone)
                {
                    return Unauthorized("Organisation not found in token claims.");
                }

                var organisationId = p.OrganisationId.ValueUnsafe().Value;
                var filter = new AuditLogFilter(action, actorUserId, targetUserId, organisationId, from, to);
                return await auditLogQueries.GetPaged(filter, page, pageSize, cancellationToken);
            },
            e => Unauthorized(e.Message));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AuditLogDetailDto>> GetById(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var permissions = permissionsService.GetOrganisationPermissions();
        return await permissions.MatchAsync<ActionResult<AuditLogDetailDto>>(
            async p =>
            {
                if (p.OrganisationId.IsNone)
                {
                    return Unauthorized("Organisation not found in token claims.");
                }

                var organisationId = p.OrganisationId.ValueUnsafe().Value;
                var result = await auditLogQueries.GetById(id, cancellationToken);
                return result.Match<ActionResult<AuditLogDetailDto>>(
                    dto => dto.OrganisationId == organisationId ? dto : NotFound(),
                    NotFound());
            },
            e => Unauthorized(e.Message));
    }
}