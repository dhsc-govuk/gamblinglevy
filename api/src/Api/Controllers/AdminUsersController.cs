using Api.Dtos;
using Api.Modules.Errors;
using Api.Services.Abstract;
using Application.Users.Commands.Admins;
using Application.Users.Commands.DeactivateUser;
using Application.Users.Commands.DeleteUser;
using Application.Users.Commands.EnableUserPasswordLogin;
using Application.Users.Commands.ReactivateUser;
using Application.Users.Commands.ResetUserMfa;
using Application.Users.Commands.ResetUserPassword;
using Domain.ValueObjects;
using LanguageExt;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[Route("admin/users")]
[ApiController]
[Authorize(Roles = Role.Admin)]
public class AdminUsersController(
    ISender sender,
    IAdminUsersControllerService controllerService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UserDto>>> GetAll(CancellationToken cancellationToken)
    {
        var result = await controllerService.GetAll(cancellationToken);

        return result.ToList();
    }

    [HttpGet("{userId:guid}")]
    public async Task<ActionResult<UserDetailDto>> Get(
        [FromRoute] Guid userId,
        CancellationToken cancellationToken)
    {
        var result = await controllerService.Get(userId, cancellationToken);

        return result.Match<ActionResult<UserDetailDto>>(
            r => r,
            NotFound());
    }

    [HttpPut("edit")]
    public async Task<ActionResult<UserDto>> Edit(
        [FromBody] UpdateUserPayload payload,
        CancellationToken cancellationToken)
    {
        var input = new AdminEditUserCommand
        {
            Id = payload.Id,
            FirstName = payload.FirstName,
            LastName = payload.LastName,
            Email = payload.Email,
            Role = payload.Role,
            OrganisationId = payload.OrganisationId,
        };

        var result = await sender.Send(input, cancellationToken);
        return result.Match<ActionResult<UserDto>>(
            user => UserDto.FromDomainModel(user),
            error => error.ToObjectResult());
    }

    [HttpPost("create")]
    public async Task<ActionResult<UserDto>> Create(
        [FromBody] CreateUserPayload payload,
        CancellationToken cancellationToken)
    {
        var input = new CreateAdminCommand
        {
            FirstName = payload.FirstName,
            LastName = payload.LastName,
            Email = payload.Email,
            IsActive = payload.IsActive ?? true,
        };

        var result = await sender.Send(input, cancellationToken);
        return result.Match<ActionResult<UserDto>>(
            user => UserDto.FromDomainModel(user),
            error => error.ToObjectResult());
    }

    [HttpPost("{userId:guid}/deactivate")]
    public async Task<ActionResult<UserDto>> Deactivate(
        [FromRoute] Guid userId,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new DeactivateUserCommand(userId), cancellationToken);
        return result.Match<ActionResult<UserDto>>(
            user => UserDto.FromDomainModel(user),
            error => error.ToObjectResult());
    }

    [HttpPost("{userId:guid}/reactivate")]
    public async Task<ActionResult<UserDto>> Reactivate(
        [FromRoute] Guid userId,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ReactivateUserCommand(userId), cancellationToken);
        return result.Match<ActionResult<UserDto>>(
            user => UserDto.FromDomainModel(user),
            error => error.ToObjectResult());
    }

    [HttpPost("{userId:guid}/reset-mfa")]
    public async Task<ActionResult<UserDto>> ResetMfa(
        [FromRoute] Guid userId,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ResetUserMfaCommand(userId), cancellationToken);
        return result.Match<ActionResult<UserDto>>(
            user => UserDto.FromDomainModel(user),
            error => error.ToObjectResult());
    }

    [HttpPost("{userId:guid}/enable-password-login")]
    public async Task<ActionResult<UserDto>> EnablePasswordLogin(
        [FromRoute] Guid userId,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new EnableUserPasswordLoginCommand(userId), cancellationToken);
        return result.Match<ActionResult<UserDto>>(
            user => UserDto.FromDomainModel(user),
            error => error.ToObjectResult());
    }

    [HttpPost("{userId:guid}/reset-password")]
    public async Task<ActionResult<UserDto>> ResetPassword(
        [FromRoute] Guid userId,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ResetUserPasswordCommand(userId), cancellationToken);
        return result.Match<ActionResult<UserDto>>(
            user => UserDto.FromDomainModel(user),
            error => error.ToObjectResult());
    }

    [HttpDelete("{userId:guid}")]
    public async Task<ActionResult> Delete(
        [FromRoute] Guid userId,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new DeleteUserCommand(userId), cancellationToken);
        return result.Match<ActionResult>(
            _ => NoContent(),
            error => error.ToObjectResult());
    }
}