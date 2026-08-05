namespace Application.Mfa.Dtos;

/// <summary>
/// One row of "where you are signed in" (BLUM-7241 / CWE-613). Everything here is display metadata the user
/// judges "was that me?" against — deliberately not the device-binding hashes, which are never exposed.
/// </summary>
/// <param name="IsCurrent">
/// Whether this is the session carrying the request that asked. Resolved from the mfa_session cookie by the
/// controller, which is the only layer that can see it, rather than by the query.
/// </param>
public record MfaSessionListItemDto(
    Guid Id,
    string DeviceLabel,
    string IpAddress,
    DateTime CreatedAt,
    DateTime LastActivityAt,
    DateTime ExpiresAt,
    bool IsCurrent = false);