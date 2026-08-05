using Api.Dtos;
using Api.Modules.Errors;
using Api.Services.Abstract;
using Application.Common.Permissions;
using Application.Organisations.Commands;
using Domain.ValueObjects;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[Route("organisations/{organisationId:guid}/locations")]
[ApiController]
[Authorize(Roles = $"{Role.Admin},{Role.OrganisationAdmin}")]
public class LocationsController(
    PermissionsService permissionsService,
    ISender sender,
    ILocationsControllerService controllerService)
    : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<LocationDto>>> GetAll(
        [FromRoute] Guid organisationId,
        CancellationToken cancellationToken)
    {
        var permissions = permissionsService.GetOrganisationPermissions();
        return await permissions.MatchAsync<ActionResult<IReadOnlyList<LocationDto>>>(
            async p =>
            {
                var result = await controllerService.GetAll(p, organisationId, cancellationToken);

                return result.ToList();
            },
            e => Unauthorized(e));
    }

    [HttpGet("{locationId:guid}")]
    public async Task<ActionResult<LocationDto>> Get(
        [FromRoute] Guid locationId,
        CancellationToken cancellationToken)
    {
        var permissions = permissionsService.GetOrganisationPermissions();
        return await permissions.MatchAsync(
            async p =>
            {
                var result = await controllerService.Get(p, locationId, cancellationToken);

                return result.Match<ActionResult<LocationDto>>(
                    r => r,
                    NotFound());
            },
            e => Unauthorized(e));
    }

    [HttpPost("create")]
    public async Task<ActionResult<LocationDto>> Create(
        [FromRoute] Guid organisationId,
        [FromBody] LocationDto location,
        CancellationToken cancellationToken)
    {
        var input = new CreateLocationCommand
        {
            OrganisationId = organisationId,
            Name = location.Name,
            IsActive = location.IsActive
        };

        var result = await sender.Send(input, cancellationToken);
        return result.Match<ActionResult<LocationDto>>(
            l => new LocationDto(l.Id.Value, l.OrganisationId.Value, l.Name, l.IsActive),
            e => e.ToObjectResult());
    }

    [HttpPut("edit")]
    public async Task<ActionResult<LocationDto>> Edit(
        [FromBody] LocationDto location,
        CancellationToken cancellationToken)
    {
        var input = new UpdateLocationCommand
        {
            Id = location.Id!.Value,
            Name = location.Name,
            IsActive = location.IsActive
        };

        var result = await sender.Send(input, cancellationToken);
        return result.Match<ActionResult<LocationDto>>(
            l => new LocationDto(l.Id.Value, l.OrganisationId.Value, l.Name, l.IsActive),
            e => e.ToObjectResult());
    }

    [HttpGet("{locationId:guid}/totals")]
    public async Task<ActionResult<LocationHomeDto>> Totals(
        [FromRoute] Guid locationId,
        CancellationToken cancellationToken)
    {
        var permissions = permissionsService.GetOrganisationPermissions();
        return await permissions.MatchAsync(
            async p =>
            {
                var result = await controllerService.Totals(p, locationId, cancellationToken);

                return result.Match<ActionResult<LocationHomeDto>>(
                    r => r,
                    NotFound());
            },
            e => Unauthorized(e));
    }
}