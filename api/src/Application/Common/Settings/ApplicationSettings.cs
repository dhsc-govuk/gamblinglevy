namespace Application.Common.Settings;

public class ApplicationSettings
{
    public AzureAdSettings AzureAd { get; set; }
    public GraphSettings Graph { get; set; }
    public DefaultAdminUserSettings DefaultAdminUser { get; set; }
    public SendGridSettings SendGrid { get; set; }
    public AzureBlobSettings AzureBlob { get; set; }

    public MfaSettings Mfa { get; set; } = new();

    public InputValidationSettings InputValidation { get; set; } = new();
}

/// <summary>
/// Controls the security-monitoring trail for rejected input (BLUM-7240 AC8).
/// </summary>
public class InputValidationSettings
{
    /// <summary>
    /// Master switch for writing audit_logs rows. Ordinary field-level failures are never audited whatever
    /// this is set to — they go to the validation_failed telemetry event and the security log.
    /// </summary>
    public bool AuditSecurityRejections { get; set; } = true;

    /// <summary>
    /// Rows one caller may generate against one endpoint per hour before further rejections are logged but
    /// not audited. Without a cap, an authenticated caller chooses how many transactions the service commits.
    /// 0 means unlimited, which is what the test settings use so assertions are not throttled.
    /// </summary>
    public int MaxAuditRowsPerActorPerHour { get; set; } = 3;

    /// <summary>
    /// Ceiling across all callers, so a probe spread over many accounts cannot flood the table either.
    /// 0 means unlimited.
    ///
    /// Per hour, not per minute. audit_logs has no retention sweep, so a per-minute ceiling of the same
    /// number would admit roughly eighty-six thousand rows a day — burying the genuine user.* and mfa.* trail
    /// under exactly the flood this setting exists to prevent, with no way to delete the rows afterwards.
    /// </summary>
    public int MaxAuditRowsPerHour { get; set; } = 60;
}

public class AzureAdSettings
{
    public string Instance { get; set; }
    public string TenantId { get; set; }
    public string ClientId { get; set; }
    public string ClientSecret { get; set; }
    public string Domain { get; set; }
}

public class GraphSettings
{
    public string TenantId { get; set; }
    public string ApplicationId { get; set; }
    public string ApplicationObjectId { get; set; }
    public string EnterpriseApplicationObjectId { get; set; }
    public string DefaultUserPassword { get; set; }
    public string Domain { get; set; }
}

public class DefaultAdminUserSettings
{
    public string Email { get; set; }
    public string SubId { get; set; }
}

public class SendGridSettings
{
    public string ClientUrl { get; set; }
    public string EmailApiKey { get; set; }
    public string SenderEmail { get; set; }
}

public class AzureBlobSettings
{
    public string ConnectionString { get; set; }
}

/// <summary>
/// Note for whoever changes SessionExpiryHours or IdleTimeoutMinutes: both numbers are also published to
/// users as literal copy, and the client has no unauthenticated endpoint to read them from. Retuning either
/// value means editing client/src/views/Public/SignIn/index.tsx (the signed-out banner) and
/// client/src/views/Public/Cookies/index.tsx (the cookie register, which is a published statement this
/// service is accountable for) in the same change.
/// </summary>
public class MfaSettings
{
    /// <summary>Absolute cap on a session, counted from sign-in and never extended by activity.</summary>
    public int SessionExpiryHours { get; set; } = 8;

    /// <summary>
    /// Inactivity window. A session that goes this long without carrying a request is destroyed on the next
    /// one (BLUM-7238 / CWE-613). Configurable so an environment can be tuned down to reproduce the finding.
    /// </summary>
    public int IdleTimeoutMinutes { get; set; } = 30;

    /// <summary>
    /// How many sessions one user may hold at once (BLUM-7241 / CWE-613). When a sign-in would exceed this,
    /// the least recently active sessions are revoked until the user is back within the cap. Set to 0 to
    /// allow unlimited concurrent sessions, which is the state the pen test found — the visibility and
    /// revocation controls still apply, but nothing is evicted automatically.
    /// </summary>
    public int MaxConcurrentSessions { get; set; } = 5;

    /// <summary>
    /// How long a finished session's record is kept before it is deleted. Since BLUM-7241 these rows carry an
    /// IP address and a device description — personal data — so this is a retention period, not just
    /// housekeeping, and the number is the sort of thing the service is accountable for publishing.
    ///
    /// It must stay comfortably longer than SessionExpiryHours. The row is what lets MfaRequiredMiddleware
    /// keep answering SESSION_EXPIRED / SESSION_IDLE_TIMEOUT / SESSION_REVOKED to a browser still presenting
    /// the cookie; delete it too early and that browser is told MFA_REQUIRED instead, which sends the user to
    /// re-enter a code — minting a fresh session and undoing the very revocation that ended the old one.
    /// Set to 0 to keep records indefinitely.
    /// </summary>
    public int SessionRetentionDays { get; set; } = 30;

    /// <summary>
    /// How often the retention sweep repeats once the app is running. 0 leaves only the sweep that runs at
    /// startup.
    ///
    /// Note that under always_on = false — how the App Service is currently configured — the site unloads
    /// after about twenty minutes idle, so in practice retention is enforced at each cold start rather than
    /// strictly on this interval. That is why the sweep runs at startup at all.
    /// </summary>
    public int SessionRetentionSweepIntervalHours { get; set; } = 6;

    /// <summary>
    /// How many records one delete statement may remove. The sweep repeats until a batch comes back short, so
    /// this bounds how long a single statement holds locks rather than how much is deleted overall.
    /// </summary>
    public int SessionRetentionSweepBatchSize { get; set; } = 500;

    public bool BypassForTesting { get; set; } = false;
}