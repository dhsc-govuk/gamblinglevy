using Api.Dtos;
using Api.Services.Abstract;
using Application.Common.Permissions;
using Domain.ValueObjects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[Route("funding")]
[ApiController]
[Authorize(Roles = Role.AnyOrganisationMember)]
public class FundingDashboardController(
    PermissionsService permissionsService,
    IFundingDashboardControllerService controllerService)
    : ControllerBase
{
    [HttpGet("dashboard")]
    public async Task<ActionResult<FundingDashboardDto>> GetDashboard(CancellationToken cancellationToken)
    {
        var permissions = permissionsService.GetOrganisationPermissions();
        return await permissions.MatchAsync<ActionResult<FundingDashboardDto>>(
            async p => await controllerService.GetDashboard(p, cancellationToken),
            e => Unauthorized(e));
    }
}