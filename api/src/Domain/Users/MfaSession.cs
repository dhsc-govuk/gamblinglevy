using Domain.Common;

namespace Domain.Users;

public class MfaSession : IEntity<MfaSessionId>
{
    public MfaSessionId Id { get; private set; }
    public UserId UserId { get; private set; }
    public string IpAddressHash { get; private set; }
    public string UserAgentHash { get; private set; }

    /// <summary>
    /// The client IP as seen when the session was created, kept in the clear so the user can be shown it on
    /// their own sessions list (BLUM-7241 / CWE-613). The hash above stays the thing the device binding is
    /// judged on — this column is display only and must never be compared for authorisation.
    /// </summary>
    public string IpAddress { get; private set; }

    /// <summary>
    /// A human-readable summary of the browser and platform ("Chrome on Windows"), resolved from the
    /// User-Agent at creation. The raw header is deliberately not stored: it is attacker-controlled, of
    /// unbounded length, and nothing this service does needs it after the label and the hash are taken.
    /// </summary>
    public string DeviceLabel { get; private set; }

    public DateTime ExpiresAt { get; private set; }
    public bool IsRevoked { get; private set; }
    public DateTime CreatedAt { get; private set; }

    /// <summary>
    /// When this session last carried a request past the MFA gate. Drives the idle window; slid forward by
    /// MfaSessionService on every validated request (throttled), so it is the server-side last-activity
    /// tracking the pen test required rather than anything the browser reports.
    /// </summary>
    public DateTime LastActivityAt { get; private set; }

    /// <summary>
    /// Why this session was destroyed, when a timeout destroyed it; null for an ordinary revocation such as
    /// logout, deactivation or suspension. Revoking on timeout would otherwise lose the reason, and the very
    /// next request — including one already in flight alongside the request that detected the timeout —
    /// would be told only that the session is revoked, which is a materially weaker answer.
    /// </summary>
    public MfaSessionEndReason? EndedReason { get; private set; }

    private MfaSession(
        MfaSessionId id,
        UserId userId,
        string ipAddressHash,
        string userAgentHash,
        string ipAddress,
        string deviceLabel,
        DateTime expiresAt,
        bool isRevoked,
        DateTime createdAt,
        DateTime lastActivityAt)
    {
        Id = id;
        UserId = userId;
        IpAddressHash = ipAddressHash;
        UserAgentHash = userAgentHash;
        IpAddress = ipAddress;
        DeviceLabel = deviceLabel;
        ExpiresAt = expiresAt;
        IsRevoked = isRevoked;
        CreatedAt = createdAt;
        LastActivityAt = lastActivityAt;
    }

    public static MfaSession New(
        MfaSessionId id,
        UserId userId,
        string ipAddressHash,
        string userAgentHash,
        string ipAddress,
        string deviceLabel,
        int expiryHours = 8)
    {
        Guard.NotNull(id, nameof(id));
        Guard.NotNull(userId, nameof(userId));
        Guard.NotNullOrEmpty(ipAddressHash, nameof(ipAddressHash));
        Guard.NotNullOrEmpty(userAgentHash, nameof(userAgentHash));
        Guard.NotNullOrEmpty(ipAddress, nameof(ipAddress));
        Guard.NotNullOrEmpty(deviceLabel, nameof(deviceLabel));

        var now = DateTime.UtcNow;

        return new MfaSession(
            id,
            userId,
            ipAddressHash,
            userAgentHash,
            ipAddress,
            deviceLabel,
            expiresAt: now.AddHours(expiryHours),
            isRevoked: false,
            createdAt: now,
            lastActivityAt: now);
    }

    /// <summary>
    /// Decides whether this session may carry the current request.
    ///
    /// Order matters. Both timeouts are judged before the device binding so that a session which has
    /// already aged out is reported — and therefore destroyed — as a timeout no matter which device
    /// presents the cookie; a replayed cookie on a session that is still within both windows falls through
    /// to DeviceMismatch and is treated exactly as it was before BLUM-7238.
    /// </summary>
    public MfaSessionStatus Evaluate(
        string currentIpHash,
        string currentUserAgentHash,
        DateTime utcNow,
        int idleTimeoutMinutes)
    {
        if (IsRevoked)
        {
            return MfaSessionStatus.Revoked;
        }

        if (utcNow > ExpiresAt)
        {
            return MfaSessionStatus.Expired;
        }

        if (utcNow - LastActivityAt > TimeSpan.FromMinutes(idleTimeoutMinutes))
        {
            return MfaSessionStatus.IdleTimeout;
        }

        if (IpAddressHash != currentIpHash || UserAgentHash != currentUserAgentHash)
        {
            return MfaSessionStatus.DeviceMismatch;
        }

        return MfaSessionStatus.Valid;
    }

    public void RecordActivity(DateTime utcNow)
    {
        LastActivityAt = utcNow;
    }

    public bool IsExpired() => DateTime.UtcNow > ExpiresAt;

    public void Revoke()
    {
        IsRevoked = true;
    }
}