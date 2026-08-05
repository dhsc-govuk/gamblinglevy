using Api.Dtos;
using Application.Mfa.Dtos;
using Domain.Users;
using LanguageExt;

namespace Api.Services.Abstract;

public interface IProfileControllerService
{
    Option<Profile> GetMe();

    /// <summary>
    /// The caller's own live sessions (BLUM-7241 / CWE-613, AC3).
    /// </summary>
    /// <param name="currentSessionId">
    /// The session carrying this request, read from the mfa_session cookie by the controller. Passed in
    /// rather than resolved here because the cookie is only visible at the HTTP layer; null when the request
    /// carries no usable cookie, in which case no row is marked as the current one.
    /// </param>
    Task<IReadOnlyList<MfaSessionListItemDto>> GetMySessionsAsync(
        UserId userId,
        MfaSessionId? currentSessionId,
        CancellationToken cancellationToken = default);
}