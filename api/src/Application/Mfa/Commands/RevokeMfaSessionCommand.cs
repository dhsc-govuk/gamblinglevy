using Application.Common.Interfaces;
using Application.Mfa.Exceptions;
using Domain.Users;
using LanguageExt;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Mfa.Commands;

/// <summary>
/// Ends one of the caller's own sessions from their sessions list (BLUM-7241 / CWE-613, AC4).
///
/// Distinct from LogoutMfaSessionCommand, which ends the session the request itself is carrying: here the
/// target is usually a different browser or device, one the user may have decided is not theirs. Revocation
/// takes effect on that session's very next request, because MfaRequiredMiddleware revalidates the row every
/// time — there is no cached token to wait out.
/// </summary>
public record RevokeMfaSessionCommand(UserId UserId, MfaSessionId SessionId)
    : IRequest<Either<MfaException, bool>>;

public class RevokeMfaSessionCommandHandler(
    IMfaSessionService sessionService,
    ILogger<RevokeMfaSessionCommandHandler> logger)
    : IRequestHandler<RevokeMfaSessionCommand, Either<MfaException, bool>>
{
    public async Task<Either<MfaException, bool>> Handle(
        RevokeMfaSessionCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await sessionService.RevokeSessionAsync(request.SessionId, request.UserId);

            switch (result)
            {
                case MfaSessionRevocationResult.NotFound:
                    return new MfaSessionNotFoundException(request.UserId, request.SessionId);

                case MfaSessionRevocationResult.NotOwned:
                    // Answered identically to "no such session" on purpose — telling the caller that an id
                    // they do not own exists would turn this endpoint into a way to enumerate other people's
                    // sessions. Worth a log line, though: nothing legitimate produces this.
                    logger.LogWarning(
                        "User {UserId} tried to revoke MFA session {SessionId}, which belongs to another user; ignoring.",
                        request.UserId.Value,
                        request.SessionId.Value);
                    return new MfaSessionNotFoundException(request.UserId, request.SessionId);

                default:
                    return true;
            }
        }
        catch (Exception exception)
        {
            return new MfaUnknownException(request.UserId, exception);
        }
    }
}