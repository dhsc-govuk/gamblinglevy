namespace Domain.Users;

/// <summary>
/// The subset of MfaSessionStatus that can destroy a session, persisted on the row so the verdict survives
/// the revocation that follows it (BLUM-7238). Deliberately narrower than MfaSessionStatus: only these two
/// outcomes end a session on their own, and storing the wider enum would allow meaningless values like
/// Valid or NotFound into the column.
/// </summary>
public enum MfaSessionEndReason
{
    Expired,
    IdleTimeout,
}