using Domain.Common;

namespace Domain.Users;

public class UserMfa : IEntity<UserMfaId>
{
    public UserMfaId Id { get; private set; }
    public UserId UserId { get; private set; }
    public string EncryptedSecret { get; private set; }
    public bool IsEnabled { get; private set; }
    public DateTime? EnabledAt { get; private set; }
    public List<string> HashedRecoveryCodes { get; private set; }
    public int FailedAttempts { get; private set; }
    public DateTime? LockedUntil { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private UserMfa(
        UserMfaId id,
        UserId userId,
        string encryptedSecret,
        bool isEnabled,
        DateTime? enabledAt,
        List<string> hashedRecoveryCodes,
        int failedAttempts,
        DateTime? lockedUntil,
        DateTime createdAt,
        DateTime? updatedAt)
    {
        Id = id;
        UserId = userId;
        EncryptedSecret = encryptedSecret;
        IsEnabled = isEnabled;
        EnabledAt = enabledAt;
        HashedRecoveryCodes = hashedRecoveryCodes;
        FailedAttempts = failedAttempts;
        LockedUntil = lockedUntil;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public static UserMfa New(UserMfaId id, UserId userId, string encryptedSecret, DateTime utcNow)
    {
        Guard.NotNull(id, nameof(id));
        Guard.NotNull(userId, nameof(userId));
        Guard.NotNullOrEmpty(encryptedSecret, nameof(encryptedSecret));

        return new UserMfa(
            id,
            userId,
            encryptedSecret,
            isEnabled: false,
            enabledAt: null,
            hashedRecoveryCodes: new List<string>(),
            failedAttempts: 0,
            lockedUntil: null,
            createdAt: utcNow,
            updatedAt: null);
    }

    public static UserMfa Create(UserId userId, DateTime utcNow)
    {
        Guard.NotNull(userId, nameof(userId));

        return new UserMfa(
            UserMfaId.New(),
            userId,
            encryptedSecret: string.Empty,
            isEnabled: false,
            enabledAt: null,
            hashedRecoveryCodes: new List<string>(),
            failedAttempts: 0,
            lockedUntil: null,
            createdAt: utcNow,
            updatedAt: null);
    }

    public void Enable(DateTime utcNow)
    {
        IsEnabled = true;
        EnabledAt = utcNow;
        FailedAttempts = 0;
        LockedUntil = null;
        UpdatedAt = utcNow;
    }

    public void Enable(List<string> hashedRecoveryCodes, DateTime utcNow)
    {
        Guard.NotNull(hashedRecoveryCodes, nameof(hashedRecoveryCodes));

        IsEnabled = true;
        EnabledAt = utcNow;
        HashedRecoveryCodes = hashedRecoveryCodes;
        FailedAttempts = 0;
        LockedUntil = null;
        UpdatedAt = utcNow;
    }

    public void Disable(DateTime utcNow)
    {
        IsEnabled = false;
        EnabledAt = null;
        EncryptedSecret = string.Empty;
        HashedRecoveryCodes = new List<string>();
        FailedAttempts = 0;
        LockedUntil = null;
        UpdatedAt = utcNow;
    }

    /// <summary>
    /// Counts one incorrect code and applies the lock when the threshold is reached (BLUM-7239 AC3).
    /// </summary>
    /// <returns>
    /// True only when <em>this</em> call applied the lock, so the caller revokes sessions and writes the
    /// lockout audit row once per lockout rather than once per subsequent failure.
    /// </returns>
    /// <remarks>
    /// Both limits are required parameters. They used to default to 5 and 30 and every call site took the
    /// defaults, so the numbers were unreachable from configuration while looking as though they were not —
    /// making them required turns that drift into a compile error.
    /// </remarks>
    public bool RecordFailedAttempt(int maxAttempts, int lockoutMinutes, DateTime utcNow)
    {
        // A lockout that has run its course ends the counting window with it. Without this the counter stayed
        // at or above the threshold forever, so the first wrong digit after a lock expired re-locked
        // immediately — a permanent lockout with no way out, and not only for codes: recovery-code
        // verification consults the same lock, so the escape hatch designed for exactly this situation was
        // shut too, leaving an administrator's mfa reset as the only route back.
        if (LockedUntil.HasValue && LockedUntil.Value <= utcNow)
        {
            FailedAttempts = 0;
            LockedUntil = null;
        }

        FailedAttempts++;
        UpdatedAt = utcNow;

        // Already locked means a caller reached here without checking, and re-locking would extend the
        // lockout on every attempt made during it — an attacker could hold a victim out indefinitely by
        // trickling guesses. maxAttempts of 0 disables the lockout entirely, matching the convention the
        // settings use elsewhere.
        if (LockedUntil.HasValue || maxAttempts <= 0 || FailedAttempts < maxAttempts)
        {
            return false;
        }

        LockedUntil = utcNow.AddMinutes(lockoutMinutes);

        return true;
    }

    public void ResetFailedAttempts(DateTime utcNow)
    {
        FailedAttempts = 0;
        LockedUntil = null;
        UpdatedAt = utcNow;
    }

    public bool IsLockedOut(DateTime utcNow) => LockedUntil.HasValue && LockedUntil.Value > utcNow;

    public void UpdateRecoveryCodes(List<string> hashedRecoveryCodes, DateTime utcNow)
    {
        Guard.NotNull(hashedRecoveryCodes, nameof(hashedRecoveryCodes));
        HashedRecoveryCodes = hashedRecoveryCodes;
        UpdatedAt = utcNow;
    }

    public void RemoveRecoveryCode(string hashedCode, DateTime utcNow)
    {
        HashedRecoveryCodes.Remove(hashedCode);
        UpdatedAt = utcNow;
    }

    public void UpdateSecret(string encryptedSecret, DateTime utcNow)
    {
        Guard.NotNullOrEmpty(encryptedSecret, nameof(encryptedSecret));
        EncryptedSecret = encryptedSecret;
        UpdatedAt = utcNow;
    }
}