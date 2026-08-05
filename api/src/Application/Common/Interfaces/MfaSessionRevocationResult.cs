namespace Application.Common.Interfaces;

/// <summary>
/// Outcome of revoking a single MFA session. Callers need to tell "no such session" apart from
/// "the session belongs to somebody else" — logout falls back to revoking all of the caller's own
/// sessions in the first case, but must leave another user's session untouched in the second.
/// </summary>
public enum MfaSessionRevocationResult
{
    /// <summary>The session existed, belonged to the caller, and is now revoked.</summary>
    Revoked,

    /// <summary>No session exists with that id.</summary>
    NotFound,

    /// <summary>The session exists but belongs to a different user; nothing was changed.</summary>
    NotOwned,
}