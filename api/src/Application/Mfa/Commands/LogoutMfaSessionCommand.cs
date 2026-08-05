using Application.Common.Interfaces;
using Application.Mfa.Exceptions;
using Domain.Users;
using LanguageExt;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Mfa.Commands;

/// <summary>
/// Destroys the caller's server-side MFA session on logout (BLUM-7242 / CWE-613). Clearing the
/// mfa_session cookie is not enough on its own — MfaRequiredMiddleware revalidates the session row on
/// every request, so a captured cookie replays successfully until the row itself is revoked.
/// </summary>
/// <param name="SessionId">
/// The session named by the mfa_session cookie, or null when the request carried no usable cookie.
/// </param>
public record LogoutMfaSessionCommand(UserId UserId, MfaSessionId? SessionId)
    : IRequest<Either<MfaException, bool>>;

public class LogoutMfaSessionCommandHandler(
    IMfaSessionService sessionService,
    ILogger<LogoutMfaSessionCommandHandler> logger)
    : IRequestHandler<LogoutMfaSessionCommand, Either<MfaException, bool>>
{
    public async Task<Either<MfaException, bool>> Handle(
        LogoutMfaSessionCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            if (request.SessionId is null)
            {
                // No cookie reached us — we cannot tell which session the caller meant, so revoke every
                // one of theirs rather than leaving a live session behind a "successful" logout.
                await sessionService.RevokeAllUserSessionsAsync(request.UserId);
                return true;
            }

            var result = await sessionService.RevokeSessionAsync(request.SessionId, request.UserId);

            switch (result)
            {
                case MfaSessionRevocationResult.NotFound:
                    // The cookie pointed at a session we have no record of; fall back to the same
                    // belt-and-braces sweep as the no-cookie case.
                    await sessionService.RevokeAllUserSessionsAsync(request.UserId);
                    break;

                case MfaSessionRevocationResult.NotOwned:
                    // Never revoke somebody else's session on their behalf, but a request presenting
                    // another user's session id is anomalous and worth surfacing.
                    logger.LogWarning(
                        "Logout for user {UserId} presented MFA session {SessionId} belonging to another user; ignoring.",
                        request.UserId.Value,
                        request.SessionId.Value);
                    break;
            }

            return true;
        }
        catch (Exception exception)
        {
            return new MfaUnknownException(request.UserId, exception);
        }
    }
}