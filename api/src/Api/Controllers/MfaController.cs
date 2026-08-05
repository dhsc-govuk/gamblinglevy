using Api.Common;
using Api.Modules.Errors;
using Application.Common.Interfaces;
using Application.Common.Permissions;
using Application.Mfa.Commands;
using Application.Mfa.Dtos;
using Application.Mfa.Queries;
using Domain.Users;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[Route("mfa")]
[ApiController]
[Authorize]
public class MfaController(
    ISender sender,
    IIdentityService identityService,
    PermissionsService permissionsService)
    : ControllerBase
{
    [HttpGet("status")]
    public async Task<ActionResult<MfaStatusDto>> GetStatus(CancellationToken cancellationToken)
    {
        var userId = permissionsService.GetUserId();
        return await userId.MatchAsync<ActionResult<MfaStatusDto>>(
            async uId =>
            {
                var query = new GetMfaStatusQuery(uId);
                var result = await sender.Send(query, cancellationToken);
                return Ok(result);
            },
            e => Task.FromResult<ActionResult<MfaStatusDto>>(Unauthorized(e.Message)));
    }

    [HttpPost("setup")]
    public async Task<ActionResult<MfaSetupResponseDto>> Setup(CancellationToken cancellationToken)
    {
        var userId = permissionsService.GetUserId();
        return await userId.MatchAsync<ActionResult<MfaSetupResponseDto>>(
            async uId =>
            {
                var email = identityService.GetEmail().Match(e => e, () => string.Empty);
                if (string.IsNullOrEmpty(email))
                {
                    return Unauthorized("Email is missing from token");
                }

                var command = new SetupMfaCommand(uId, email);
                var result = await sender.Send(command, cancellationToken);

                return result.Match<ActionResult<MfaSetupResponseDto>>(
                    dto => Ok(dto),
                    error => error.ToObjectResult());
            },
            e => Task.FromResult<ActionResult<MfaSetupResponseDto>>(Unauthorized(e.Message)));
    }

    [HttpPost("setup/verify")]
    public async Task<ActionResult<MfaSetupCompleteResponseDto>> VerifySetup(
        [FromBody] MfaVerifyRequestDto request,
        CancellationToken cancellationToken)
    {
        var userId = permissionsService.GetUserId();
        return await userId.MatchAsync<ActionResult<MfaSetupCompleteResponseDto>>(
            async uId =>
            {
                var ipAddress = GetClientIpAddress();
                var userAgent = GetUserAgent();

                var command = new VerifyMfaSetupCommand(uId, request.Code, ipAddress, userAgent);
                var result = await sender.Send(command, cancellationToken);

                return result.Match<ActionResult<MfaSetupCompleteResponseDto>>(
                    dto =>
                    {
                        SetMfaSessionCookie(dto.SessionId, dto.SessionExpiresAt);
                        return Ok(dto);
                    },
                    error => error.ToObjectResult());
            },
            e => Task.FromResult<ActionResult<MfaSetupCompleteResponseDto>>(Unauthorized(e.Message)));
    }

    [HttpPost("verify")]
    public async Task<ActionResult<MfaVerifyResponseDto>> Verify(
        [FromBody] MfaVerifyRequestDto request,
        CancellationToken cancellationToken)
    {
        var userId = permissionsService.GetUserId();
        return await userId.MatchAsync<ActionResult<MfaVerifyResponseDto>>(
            async uId =>
            {
                var ipAddress = GetClientIpAddress();
                var userAgent = GetUserAgent();

                var command = new VerifyMfaCommand(uId, request.Code, ipAddress, userAgent);
                var result = await sender.Send(command, cancellationToken);

                return result.Match<ActionResult<MfaVerifyResponseDto>>(
                    dto =>
                    {
                        SetMfaSessionCookie(dto.SessionId, dto.ExpiresAt);
                        return Ok(dto);
                    },
                    error => error.ToObjectResult());
            },
            e => Task.FromResult<ActionResult<MfaVerifyResponseDto>>(Unauthorized(e.Message)));
    }

    [HttpPost("verify/recovery")]
    public async Task<ActionResult<MfaVerifyResponseDto>> VerifyRecoveryCode(
        [FromBody] MfaVerifyRequestDto request,
        CancellationToken cancellationToken)
    {
        var userId = permissionsService.GetUserId();
        return await userId.MatchAsync<ActionResult<MfaVerifyResponseDto>>(
            async uId =>
            {
                var ipAddress = GetClientIpAddress();
                var userAgent = GetUserAgent();

                var command = new VerifyRecoveryCodeCommand(uId, request.Code, ipAddress, userAgent);
                var result = await sender.Send(command, cancellationToken);

                return result.Match<ActionResult<MfaVerifyResponseDto>>(
                    dto =>
                    {
                        SetMfaSessionCookie(dto.SessionId, dto.ExpiresAt);
                        return Ok(dto);
                    },
                    error => error.ToObjectResult());
            },
            e => Task.FromResult<ActionResult<MfaVerifyResponseDto>>(Unauthorized(e.Message)));
    }

    [HttpPost("disable")]
    public async Task<ActionResult> Disable(
        [FromBody] MfaVerifyRequestDto request,
        CancellationToken cancellationToken)
    {
        var userId = permissionsService.GetUserId();
        return await userId.MatchAsync<ActionResult>(
            async uId =>
            {
                var command = new DisableMfaCommand(uId, request.Code);
                var result = await sender.Send(command, cancellationToken);

                return result.Match<ActionResult>(
                    _ =>
                    {
                        ClearMfaSessionCookie();
                        return NoContent();
                    },
                    error => error.ToObjectResult());
            },
            e => Task.FromResult<ActionResult>(Unauthorized(e.Message)));
    }

    [HttpPost("logout")]
    public async Task<ActionResult> Logout(CancellationToken cancellationToken)
    {
        // Read the cookie, then clear it up front. Doing it before the command runs means the browser is
        // never left holding a session cookie because revocation threw somewhere in the MediatR pipeline.
        var sessionId = TryReadMfaSessionCookie();
        ClearMfaSessionCookie();

        var userId = permissionsService.GetUserId();
        return await userId.MatchAsync<ActionResult>(
            async uId =>
            {
                var result = await sender.Send(new LogoutMfaSessionCommand(uId, sessionId), cancellationToken);

                return result.Match<ActionResult>(
                    _ => NoContent(),
                    error => error.ToObjectResult());
            },
            e => Task.FromResult<ActionResult>(Unauthorized(e.Message)));
    }

    [HttpPost("recovery-codes/regenerate")]
    public async Task<ActionResult<MfaRecoveryCodesDto>> RegenerateRecoveryCodes(
        [FromBody] MfaVerifyRequestDto request,
        CancellationToken cancellationToken)
    {
        var userId = permissionsService.GetUserId();
        return await userId.MatchAsync<ActionResult<MfaRecoveryCodesDto>>(
            async uId =>
            {
                var command = new RegenerateRecoveryCodesCommand(uId, request.Code);
                var result = await sender.Send(command, cancellationToken);

                return result.Match<ActionResult<MfaRecoveryCodesDto>>(
                    dto => Ok(dto),
                    error => error.ToObjectResult());
            },
            e => Task.FromResult<ActionResult<MfaRecoveryCodesDto>>(Unauthorized(e.Message)));
    }

    private string GetClientIpAddress() => ClientIpAddress.Resolve(Request);

    private string GetUserAgent()
    {
        return Request.Headers.UserAgent.ToString();
    }

    private MfaSessionId? TryReadMfaSessionCookie() => MfaSessionCookie.TryRead(Request);

    private void SetMfaSessionCookie(Guid sessionId, DateTime expiresAt) =>
        MfaSessionCookie.Set(Response, sessionId, expiresAt);

    private void ClearMfaSessionCookie() => MfaSessionCookie.Clear(Response);
}