using Api.Dtos;
using Api.Modules.Errors;
using Api.Services.Abstract;
using Application.Common.Permissions;
using Application.Users.Commands.OrganisationUsers;
using Domain.ValueObjects;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[Route("organisations/{organisationId:guid}/users")]
[ApiController]
// Reads are open to every LA role so a Lead can view its own LA's users (BLUM-7038 AC3); the mutating
// actions below each narrow to Role.UserManagers. Org-scoping comes from PermissionsService +
// OrganisationUserRepository.GetQuery, so a Lead only ever sees its own organisation.
[Authorize(Roles = $"{Role.Admin},{Role.AnyOrganisationMember}")]
public class OrganisationUsersController(
    PermissionsService permissionsService,
    ISender sender,
    IOrganisationUsersControllerService controllerService)
    : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UserDto>>> GetAll(
        [FromRoute] Guid organisationId,
        CancellationToken cancellationToken)
    {
        var permissions = permissionsService.GetOrganisationPermissions();
        return await permissions.MatchAsync<ActionResult<IReadOnlyList<UserDto>>>(
            async p =>
            {
                var result = await controllerService.GetAll(p, organisationId, cancellationToken);

                return result.ToList();
            },
            e => Unauthorized(e.Message));
    }

    [HttpGet("{userId:guid}")]
    public async Task<ActionResult<UserDetailDto>> Get(
        [FromRoute] Guid userId,
        CancellationToken cancellationToken)
    {
        var permissions = permissionsService.GetOrganisationPermissions();
        return await permissions.MatchAsync(
            async p =>
            {
                var result = await controllerService.Get(p, userId, cancellationToken);
                return result.Match<ActionResult<UserDetailDto>>(
                    r => r,
                    NotFound());
            },
            e => Unauthorized(e.Message));
    }

    [Authorize(Roles = Role.UserManagers)]
    [HttpPost("create")]
    public async Task<ActionResult<UserDto>> Create(
        [FromBody] UserDto user,
        [FromRoute] Guid organisationId,
        CancellationToken cancellationToken)
    {
        var input = new CreateOrganisationUserCommand
        {
            FirstName = user.FirstName,
            LastName = user.LastName,
            Email = user.Email,
            IsActive = string.Equals(user.Status, "active", StringComparison.OrdinalIgnoreCase),
            OrganisationId = organisationId,
            Role = user.Role
        };

        var result = await sender.Send(input, cancellationToken);
        return result.Match<ActionResult<UserDto>>(
            u => UserDto.FromDomainModel(u),
            error => error.ToObjectResult());
    }

    [Authorize(Roles = Role.UserManagers)]
    [HttpPut("edit")]
    public async Task<ActionResult<UserDto>> Edit(
        [FromBody] UserDto user,
        CancellationToken cancellationToken)
    {
        var input = new UpdateOrganisationUserCommand
        {
            Id = user.Id!.Value,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Email = user.Email,
            IsActive = string.Equals(user.Status, "active", StringComparison.OrdinalIgnoreCase),
            Role = user.Role
        };

        var result = await sender.Send(input, cancellationToken);
        return result.Match<ActionResult<UserDto>>(
            u => UserDto.FromDomainModel(u),
            error => error.ToObjectResult());
    }

    [Authorize(Roles = Role.UserManagers)]
    [HttpPost("{userId:guid}/deactivate")]
    public async Task<ActionResult<UserDto>> Deactivate(
        [FromRoute] Guid organisationId,
        [FromRoute] Guid userId,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new DeactivateOrganisationUserCommand(organisationId, userId), cancellationToken);
        return result.Match<ActionResult<UserDto>>(
            u => UserDto.FromDomainModel(u),
            error => error.ToObjectResult());
    }

    [Authorize(Roles = Role.UserManagers)]
    [HttpPost("{userId:guid}/reactivate")]
    public async Task<ActionResult<UserDto>> Reactivate(
        [FromRoute] Guid organisationId,
        [FromRoute] Guid userId,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ReactivateOrganisationUserCommand(organisationId, userId), cancellationToken);
        return result.Match<ActionResult<UserDto>>(
            u => UserDto.FromDomainModel(u),
            error => error.ToObjectResult());
    }

    [Authorize(Roles = Role.UserManagers)]
    [HttpDelete("{userId:guid}")]
    public async Task<ActionResult> Delete(
        [FromRoute] Guid organisationId,
        [FromRoute] Guid userId,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new DeleteOrganisationUserCommand(organisationId, userId), cancellationToken);
        return result.Match<ActionResult>(
            _ => NoContent(),
            error => error.ToObjectResult());
    }

    [Authorize(Roles = Role.UserManagers)]
    [HttpPost("{userId:guid}/reset-mfa")]
    public async Task<ActionResult<UserDto>> ResetMfa(
        [FromRoute] Guid organisationId,
        [FromRoute] Guid userId,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ResetOrganisationUserMfaCommand(organisationId, userId), cancellationToken);
        return result.Match<ActionResult<UserDto>>(
            u => UserDto.FromDomainModel(u),
            error => error.ToObjectResult());
    }
}