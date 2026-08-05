using Api.Common;
using Api.Dtos;
using Api.Modules.Errors;
using Api.Services.Abstract;
using Application.Common.Permissions;
using Application.Mfa.Commands;
using Application.Mfa.Dtos;
using Domain.Users;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[Route("profile")]
[ApiController]
[Authorize]
public class ProfileController(
    IProfileControllerService controllerService,
    ISender sender,
    PermissionsService permissionsService)
    : ControllerBase
{
    [HttpGet("me")]
    public ActionResult<Profile> GetMe()
    {
        var result = controllerService.GetMe();

        return result.Match<ActionResult<Profile>>(
            r => r,
            NotFound());
    }

    /// <summary>
    /// The caller's own live sessions (BLUM-7241 / CWE-613, AC3).
    ///
    /// Deliberately on /profile rather than /mfa: MfaRequiredMiddleware exempts every /mfa/* path, so a
    /// sessions endpoint there would keep answering for a session that had just been revoked — which is
    /// precisely the state this feature exists to end. No role gate, because every role has sessions.
    /// </summary>
    [HttpGet("sessions")]
    public async Task<ActionResult<IReadOnlyList<MfaSessionListItemDto>>> GetMySessions(CancellationToken cancellationToken)
    {
        var userId = permissionsService.GetUserId();
        return await userId.MatchAsync<ActionResult<IReadOnlyList<MfaSessionListItemDto>>>(
            async uId =>
            {
                var sessions = await controllerService.GetMySessionsAsync(
                    uId,
                    MfaSessionCookie.TryRead(Request),
                    cancellationToken);

                return Ok(sessions);
            },
            e => Task.FromResult<ActionResult<IReadOnlyList<MfaSessionListItemDto>>>(Unauthorized(e.Message)));
    }

    /// <summary>
    /// Ends one of the caller's own sessions (AC4). Ownership is enforced in the handler, and a session
    /// belonging to somebody else is answered exactly as a non-existent one.
    /// </summary>
    [HttpPost("sessions/{sessionId:guid}/revoke")]
    public async Task<ActionResult> RevokeSession(Guid sessionId, CancellationToken cancellationToken)
    {
        var userId = permissionsService.GetUserId();
        return await userId.MatchAsync<ActionResult>(
            async uId =>
            {
                var target = new MfaSessionId(sessionId);
                var result = await sender.Send(new RevokeMfaSessionCommand(uId, target), cancellationToken);

                return result.Match<ActionResult>(
                    _ =>
                    {
                        // Revoking the session this very request is carrying is a sign-out. Drop the cookie
                        // too, otherwise the browser keeps presenting a session the server has destroyed and
                        // every subsequent request comes back SESSION_REVOKED.
                        if (MfaSessionCookie.TryRead(Request) == target)
                        {
                            MfaSessionCookie.Clear(Response);
                        }

                        return NoContent();
                    },
                    error => error.ToObjectResult());
            },
            e => Task.FromResult<ActionResult>(Unauthorized(e.Message)));
    }
}