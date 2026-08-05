using Domain.Users;

namespace Application.Common.Interfaces;

public interface IMfaSessionService
{
    string HashIpAddress(string ipAddress);
    string HashUserAgent(string userAgent);
    Task<MfaSession> CreateSessionAsync(UserId userId, string ipAddress, string userAgent);
    Task<MfaSessionStatus> ValidateSessionAsync(MfaSessionId sessionId, UserId userId, string ipAddress, string userAgent);
    Task<MfaSessionRevocationResult> RevokeSessionAsync(MfaSessionId sessionId, UserId userId);
    Task RevokeAllUserSessionsAsync(UserId userId);
}