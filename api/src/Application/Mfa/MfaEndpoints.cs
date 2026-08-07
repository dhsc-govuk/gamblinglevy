namespace Application.Mfa;

/// <summary>
/// The five endpoints that accept a user-supplied code, named for the audit trail (BLUM-7239 AC3).
///
/// They exist because the audit <em>action</em> cannot carry this. Most of these check a TOTP, so most of
/// them legitimately record <c>mfa.verify.failed</c> — which meant that until the endpoint became its own
/// field, a brute force against /mfa/disable was indistinguishable in audit_logs from one against the
/// ordinary sign-in code entry, and the lockout row named its endpoint as "mfa.verify.failed".
///
/// Constants rather than literals at the four call sites so the strings cannot drift apart, and route-shaped
/// rather than prose so they can be matched against the route templates the rate limiter buckets on.
/// </summary>
public static class MfaEndpoints
{
    public const string Verify = "mfa/verify";
    public const string VerifyRecovery = "mfa/verify/recovery";
    public const string SetupVerify = "mfa/setup/verify";
    public const string Disable = "mfa/disable";
    public const string RegenerateRecoveryCodes = "mfa/recovery-codes/regenerate";
}