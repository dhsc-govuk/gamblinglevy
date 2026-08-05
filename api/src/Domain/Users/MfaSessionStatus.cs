namespace Domain.Users;

/// <summary>
/// Why a presented mfa_session cookie was accepted or refused. The caller needs the reason, not just a
/// boolean: an aged-out session has to be destroyed and reported as a timeout (BLUM-7238 / CWE-613),
/// whereas an unknown or hijacked cookie is simply told to verify again.
/// </summary>
public enum MfaSessionStatus
{
    Valid,
    NotFound,
    NotOwned,
    Revoked,
    Expired,
    IdleTimeout,
    DeviceMismatch,
}