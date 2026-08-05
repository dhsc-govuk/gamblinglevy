using Api.Dtos;
using Api.Services.Abstract;
using Application.Common.Permissions;
using Domain.ValueObjects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[Route("organisation-admin")]
[ApiController]
// Lead shares the LA Admin home surface (BLUM-7038) — the user-management actions it must not reach are
// gated on OrganisationUsersController, not here.
[Authorize(Roles = Role.AnyOrganisationMember)]
public class OrganisationManagerHomeController(
    PermissionsService permissionsService,
    IOrganisationManagerHomeControllerService controllerService)
    : ControllerBase
{
    [HttpGet("totals")]
    public async Task<ActionResult<OrganisationAdminHomeDto>> GetTotals(CancellationToken cancellationToken)
    {
        var permissions = permissionsService.GetOrganisationPermissions();
        return await permissions.MatchAsync<ActionResult<OrganisationAdminHomeDto>>(
            async p => await controllerService.GetTotals(p, cancellationToken),
            e => Unauthorized(e));
    }
}