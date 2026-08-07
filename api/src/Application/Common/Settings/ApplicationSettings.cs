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

    public RateLimitingSettings RateLimiting { get; set; } = new();
}

/// <summary>
/// Per-IP request limits (BLUM-7239 / CWE-770).
///
/// Two things about how these are enforced are worth knowing before retuning any of them.
///
/// The counters live in one process's memory. The API runs on a single B1 App Service with always_on = false,
/// so the site unloads after about twenty minutes idle and every counter resets; if the plan is ever scaled
/// out, the effective limit multiplies by the instance count. These are a fair-use control and a load shed,
/// not a security boundary. The remedies, in order of preference, are enforcement at an edge tier or a
/// distributed counter — neither exists today.
///
/// And the caller's IP is whatever the caller says it is. It is read from X-Azure-ClientIP or
/// X-Forwarded-For, and nothing yet proves the request actually arrived through Front Door.
/// IpAddressNormaliser stops one address being spelled several ways to mint several buckets, but not a
/// caller claiming a fresh address per request. <see cref="Global"/> is the only in-app answer to that.
/// </summary>
public class RateLimitingSettings
{
    /// <summary>
    /// Master switch. False disables counting entirely — no headers, no 429, no allocation. The integration
    /// suite turns it off globally, because the counters outlive an individual test.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Safe reads. Set high because the SPA fans out several parallel GETs on a single page load, and
    /// because Local Authorities behind one NAT egress all present the same address — a false positive here
    /// locks out an entire organisation rather than one user.
    /// </summary>
    public RateLimitPolicySettings Read { get; set; } = new(300, 60);

    /// <summary>
    /// Anything that changes state. The heaviest legitimate flow is a report page saving answers, which the
    /// client debounces, so nothing a person does approaches one write a second.
    /// </summary>
    public RateLimitPolicySettings Write { get; set; } = new(60, 60);

    /// <summary>
    /// Exports and bulk imports, which are neither reads nor ordinary writes however they are spelled in
    /// HTTP. An export builds a ZIP of per-section CSVs across every organisation in the collection, and the
    /// two bulk imports accept 20 MB uploads. On a B1 a handful of concurrent ones is a memory-pressure DoS
    /// — the same class of problem BLUM-7240 addressed by dropping Kestrel's body limit from 1000 MB.
    /// </summary>
    public RateLimitPolicySettings Export { get; set; } = new(10, 300);

    /// <summary>
    /// The MFA endpoints that take a credential. Deliberately looser than the per-user limit inside
    /// MfaSettings, so that for a single account the per-user control stays the binding one and this catches
    /// what it structurally cannot see: one address spraying codes across many accounts.
    ///
    /// Sized against the shared-NAT case rather than against one attacker, for the same reason
    /// <see cref="Read"/> is: a Local Authority's staff all present one egress address, so a morning sign-in
    /// wave is dozens of code submissions a minute from what looks like a single caller. At the original 20
    /// that refused verification for a whole organisation. Note that this budget no longer covers GET
    /// /mfa/status — the resolver sends that to Read, because a status poll is not a credential.
    /// </summary>
    public RateLimitPolicySettings Auth { get; set; } = new(60, 60);

    /// <summary>
    /// An unpartitioned ceiling for the whole process, and the only in-app mitigation for a caller who
    /// varies the address they claim. It is load-shedding, not attribution: it cannot tell who is
    /// responsible, so it is deliberately set where a B1 is already in trouble rather than anywhere near
    /// where legitimate traffic lives. Review against real request volume before production.
    /// </summary>
    public RateLimitPolicySettings Global { get; set; } = new(1200, 60);

    /// <summary>
    /// How many partitions the limiter's private cache may hold, roughly a kilobyte each. Bounds the
    /// cardinality an attacker can create by varying their claimed address. When the cache compacts, the
    /// evicted caller's count restarts — the limiter fails open, which is the right way for a fair-use
    /// control to fail.
    /// </summary>
    public int MaxTrackedPartitions { get; set; } = 20_000;

    /// <summary>Master switch for writing audit_logs rows. Rejections still reach telemetry and the log without it.</summary>
    public bool AuditRateLimitEvents { get; set; } = true;

    /// <summary>
    /// Rows one caller may generate against one endpoint per hour. 0 means unlimited, which is what the test
    /// settings use.
    /// </summary>
    public int MaxAuditRowsPerActorPerHour { get; set; } = 3;

    /// <summary>
    /// Ceiling across all callers. Per hour, not per minute, for the reason InputValidationSettings gives:
    /// audit_logs has no retention sweep, so a per-minute ceiling of the same number would admit roughly
    /// eighty-six thousand rows a day and bury the genuine user.* and mfa.* trail under exactly the flood
    /// this setting exists to prevent. 0 means unlimited.
    /// </summary>
    public int MaxAuditRowsPerHour { get; set; } = 60;
}

/// <summary>One policy's budget.</summary>
/// <param name="PermitLimit">Requests allowed per window. 0 or less means unlimited, matching the convention in InputValidationSettings.</param>
/// <param name="WindowSeconds">How long the budget runs for before it resets.</param>
public record RateLimitPolicySettings(int PermitLimit, int WindowSeconds)
{
    // Parameterless ctor so the configuration binder can construct one; the positional record ctor supplies
    // the defaults above. Named properties rather than a dictionary of policies is also deliberate: the
    // binder overlays a partial JSON block onto these defaults, so an environment overriding one policy
    // keeps the rest. Overriding one entry of a bound dictionary replaces the whole map, which would
    // silently unlimit every policy the environment did not restate.
    public RateLimitPolicySettings()
        : this(0, 60)
    {
    }

    public TimeSpan Window => TimeSpan.FromSeconds(this.WindowSeconds <= 0 ? 60 : this.WindowSeconds);
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
/// Note for whoever changes SessionExpiryHours, IdleTimeoutMinutes or LockoutMinutes: all three numbers are
/// also published to users as literal copy, and the client has no unauthenticated endpoint to read them
/// from. Retuning any of them means editing client/src/views/Public/SignIn/index.tsx (the signed-out banner,
/// which states the lockout duration) and client/src/views/Public/Cookies/index.tsx (the cookie register,
/// which is a published statement this service is accountable for) in the same change.
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

    /// <summary>
    /// Consecutive incorrect codes that lock the account (BLUM-7239 AC3).
    ///
    /// Counted per user, across every device and every MFA endpoint that takes a code. What is being guessed
    /// — one TOTP secret, one set of recovery-code hashes — belongs to the user, so a per-device budget would
    /// simply multiply an attacker's allowance by the number of User-Agent strings they cared to send. The
    /// lock applies ON the Nth failure, not after it.
    ///
    /// Note that this counter is persisted on the user_mfas row rather than held in memory, so unlike
    /// <see cref="MaxAttemptsPerWindow"/> it survives a restart and would survive a scale-out. That is why it,
    /// and not the per-minute limiter, is the control of record. 0 disables the lockout.
    /// </summary>
    public int MaxFailedAttempts { get; set; } = 5;

    /// <summary>
    /// How long the lock holds, and the ceiling on the Retry-After this API will state.
    ///
    /// Reaching the lock also revokes the user's live sessions, so every other tab is signed out and has to
    /// re-authenticate — which is what "session locked requiring re-auth" means in practice. The residual
    /// risk that buys: someone who already holds the user's password can keep them locked out. That is
    /// accepted deliberately, because the alternative — a lockout that leaves an established session running
    /// — is the same fail-open posture BLUM-7238 and BLUM-7241 were raised to close.
    /// </summary>
    public int LockoutMinutes { get; set; } = 30;

    /// <summary>
    /// A burst damper in front of the lockout, counted per user in memory over
    /// <see cref="RateLimitWindowSeconds"/>. 0 means unlimited — which is what the lockout tests need, since
    /// a limiter and a lockout that both trip at five would mean the lockout could never be observed.
    /// </summary>
    public int MaxAttemptsPerWindow { get; set; } = 5;

    /// <summary>The damper's window.</summary>
    public int RateLimitWindowSeconds { get; set; } = 60;

    public bool BypassForTesting { get; set; } = false;
}