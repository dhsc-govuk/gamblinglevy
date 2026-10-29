using Domain.Users;

namespace Tests.Data;

// MFA rows for tests that need a user to already be enrolled or signed in, without walking mfa/setup and
// mfa/setup/verify first. The secret and the hashes are placeholders: nothing built here can pass a TOTP
// check or the device binding, so use the API route (see the Mfa/* tests) when a test verifies a code.
public static class MfaData
{
    // An enrolment the user finished: a user_mfas row with IsEnabled set.
    public static UserMfa Enabled(UserId userId)
    {
        var mfa = Pending(userId);
        mfa.Enable(["hashed-recovery-code"], DateTime.UtcNow);

        return mfa;
    }

    // An enrolment the user started and abandoned: the row exists, IsEnabled does not.
    public static UserMfa Pending(UserId userId) =>
        UserMfa.New(UserMfaId.New(), userId, "encrypted-secret", DateTime.UtcNow);

    public static MfaSession Session(UserId userId) =>
        MfaSession.New(
            MfaSessionId.New(),
            userId,
            ipAddressHash: "seeded-ip-hash",
            userAgentHash: "seeded-user-agent-hash",
            ipAddress: "203.0.113.10",
            deviceLabel: "Chrome on Windows",
            utcNow: DateTime.UtcNow);
}