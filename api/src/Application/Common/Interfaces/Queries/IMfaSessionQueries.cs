using Application.Mfa.Dtos;
using Domain.Users;

namespace Application.Common.Interfaces.Queries;

public interface IMfaSessionQueries
{
    /// <summary>
    /// The user's own live sessions, most recently active first (BLUM-7241).
    ///
    /// "Live" here means the same thing MfaSession.Evaluate means by it — not revoked, inside the absolute
    /// window and inside the idle window. Timed-out sessions are only revoked lazily, on the next request
    /// that presents them, so filtering on IsRevoked alone would list sessions that are already dead and
    /// invite the user to revoke something that cannot carry a request anyway.
    /// </summary>
    Task<IReadOnlyList<MfaSessionListItemDto>> GetActiveForUserAsync(UserId userId, CancellationToken cancellationToken = default);
}