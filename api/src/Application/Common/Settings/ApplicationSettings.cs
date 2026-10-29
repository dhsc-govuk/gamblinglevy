namespace Application.Common.Settings;

public class ApplicationSettings
{
    public AzureAdSettings AzureAd { get; set; }
    public GraphSettings Graph { get; set; }
    public DefaultAdminUserSettings DefaultAdminUser { get; set; }
    public SendGridSettings SendGrid { get; set; }
    public NotifySettings Notify { get; set; }

    /// <summary>
    /// Which transport <see cref="Infrastructure.Email.ConfigureEmailService"/> resolves for
    /// IInvitationEmailService / ISubmissionConfirmationEmailService / INewSignInEmailService. Defaults to the
    /// existing SendGrid transport everywhere, so introducing GOV.UK Notify does not change behaviour in any
    /// environment until this is explicitly set to <see cref="EmailProvider.Notify"/>.
    /// </summary>
    public EmailProvider EmailProvider { get; set; } = EmailProvider.SendGrid;

    public AzureBlobSettings AzureBlob { get; set; }

    public MfaSettings Mfa { get; set; } = new();

    public InputValidationSettings InputValidation { get; set; } = new();

    public RateLimitingSettings RateLimiting { get; set; } = new();

    public DataResetSettings DataReset { get; set; } = new();

    public SeedingSettings Seeding { get; set; } = new();

    public BulkImportSettings BulkImport { get; set; } = new();

    public CollectionNotificationSettings CollectionNotifications { get; set; } = new();

    public DatabaseSettings Database { get; set; } = new();

    public AuditSettings Audit { get; set; } = new();

    /// <summary>
    /// Gets or sets the per-environment feature flags, keyed by <see cref="Application.Common.Features.Feature"/> name
    /// (<c>Features__BLUM_7637=true</c>). A flag that is absent is off. Only <c>true</c> and
    /// <c>false</c> are valid values — the binder silently drops anything else, leaving the flag off, which is why
    /// the boot log warns about it (Api.Common.FeatureFlagConfiguration). Read it through
    /// <see cref="Application.Common.Features.IFeatureFlags"/>, never directly.
    /// </summary>
    public Dictionary<string, bool> Features { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Gates Postgres authentication for DAC (BLUM-7758).
/// </summary>
public class DatabaseSettings
{
    /// <summary>
    /// Gets or sets a value indicating whether <see cref="Infrastructure.Persistence.ConfigurePersistenceServices"/>
    /// fetches the Postgres password from Azure AD via a token credential instead of using the password embedded in
    /// ConnectionStrings:DefaultConnection. Defaults to false so attaching a managed identity to the App Service
    /// does not, by itself, change how any environment authenticates — it stays opt-in per environment. When
    /// enabled, the connection string's Username must be the Postgres AAD principal name the identity was granted
    /// (any Password segment is then ignored), and the flexible server must have that principal added as an AAD
    /// admin/role — both are infra-side prerequisites this flag does not create on its own.
    /// </summary>
    public bool UseManagedIdentity { get; set; } = false;

    /// <summary>
    /// Gets or sets the client ID of a user-assigned managed identity. Leave null/empty to use the App Service's
    /// system-assigned identity instead, via DefaultAzureCredential's normal fallback chain.
    /// </summary>
    public string? ManagedIdentityClientId { get; set; }
}

/// <summary>
/// Bounds the organisation bulk import's user-provisioning phase (BLUM-7609 follow-up).
///
/// Creating one LA user costs two Microsoft Graph round trips and a SendGrid send, and the full estate is
/// ~153 rows. Done one at a time that is a multi-minute request against Azure's 230s idle timeout — and when
/// that trips the caller gets a gateway error rather than the results, while the organisations and users
/// already written stay written, because the import is deliberately not transactional.
///
/// Three knobs. <see cref="MaxConcurrentUserCreations"/> makes it faster.
/// <see cref="UserProvisioningBudgetSeconds"/> stops new users from starting, and
/// <see cref="UserProvisioningTimeoutSeconds"/> bounds the ones already running — neither alone bounds the
/// response, because six workers can each begin a user just before the budget expires and one user can retry
/// against Graph for minutes. Together the worst case is budget + per-user.
///
/// The organisation import runs first and is database-only, so at the ~153-row estate this is written for it
/// is finished long before either ceiling matters. That is a statement about the expected input, not a
/// property of the design: nothing caps the row count, only the 10 MB upload limit, so a far larger file
/// would spend its time in the sequential phase where these knobs have no reach.
/// </summary>
public class BulkImportSettings
{
    /// <summary>
    /// Gets or sets how many users are provisioned at once. Each worker takes its own DI scope, so this is also
    /// the number of concurrent DbContexts, Npgsql connections, Graph clients and Entra token fetches an import
    /// costs — which is why the pool is fixed-size rather than one scope per row.
    /// </summary>
    public int MaxConcurrentUserCreations { get; set; } = 6;

    /// <summary>
    /// Gets or sets the wall-clock the provisioning phase may spend before it stops starting new users. Well
    /// under the 230s platform ceiling on purpose: it is the budget for the phase, and the response still has
    /// to be serialised and written afterwards.
    /// </summary>
    public int UserProvisioningBudgetSeconds { get; set; } = 150;

    /// <summary>
    /// Gets the ceiling on any single user. Generous next to the ~1-2s a healthy create costs, because the
    /// Graph SDK's own retry handler honours a Retry-After before this ever fires; it is here to stop one
    /// throttled directory write holding the whole response open, not to police normal latency.
    /// </summary>
    public int UserProvisioningTimeoutSeconds { get; set; } = 30;
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
    /// audit_logs rows stay for AuditSettings.RetentionDays (for ever by default), so a per-minute ceiling of the same number would admit roughly
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
/// Gates the test-only "reset to seed data" endpoint (truncates every application table and re-runs
/// <c>ApplicationDbContextInitialiser.SeedDataAsync</c>). Deliberately not a real user role: it exists so an
/// automated test harness can reset the database before any user session exists, so it is checked instead of
/// <c>[Authorize]</c>. Both checks fail closed — <see cref="Enabled"/> defaults false and an empty
/// <see cref="Secret"/> never matches — so a Production/UAT environment that never sets this section cannot
/// reach the endpoint no matter what header a caller presents.
/// </summary>
public class DataResetSettings
{
    /// <summary>Gets or sets a value indicating whether the reset endpoint is reachable at all. Only appsettings.Test.json and appsettings.Development.json turn this on.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Gets or sets the shared secret the caller must present in the <c>X-Data-Reset-Secret</c> header. A missing or
    /// mismatched secret answers 404, matching the "not enabled" response so the endpoint's existence isn't
    /// distinguishable from a wrong secret.
    /// </summary>
    public string? Secret { get; set; }
}

/// <summary>
/// How a failed seeding step at boot is treated (BLUM-8749).
/// </summary>
public class SeedingSettings
{
    /// <summary>
    /// Gets or sets a value indicating whether a seeding step that fails at boot stops the app from starting.
    /// Off by default, and it must stay off on deployed environments. With it on, one drifted reference row
    /// crash-loops a live service. With it off, the failure is logged and the app starts on the reference data it
    /// already holds. Migrations stop startup whatever this says. The integration suite turns it on in code
    /// (Tests.Common.TestFactory), so a seeding defect fails every test class instead of hiding behind a log line.
    /// </summary>
    public bool FailStartupOnError { get; set; } = false;

    /// <summary>
    /// Gets or sets how long, in seconds, a seeding run waits for the seeding lock while another instance holds it.
    /// When the wait runs out, this instance skips seeding and records it as a failed step. The other instance is
    /// seeding the same database, so nothing is lost. Seeding normally takes seconds, so the two-minute default is
    /// only reached when the other instance is stuck.
    /// </summary>
    public int LockWaitSeconds { get; set; } = 120;
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
    /// Per hour, not per minute. audit_logs rows stay for AuditSettings.RetentionDays — for ever by default,
    /// and years when it is set — so a per-minute ceiling of the same number would admit roughly eighty-six
    /// thousand rows a day, burying the genuine user.* and mfa.* trail under exactly the flood this setting
    /// exists to prevent, for as long as the trail itself is kept.
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

/// <summary>
/// GOV.UK Notify transport settings. Notify templates the message server-side by <c>TemplateId</c>, so — unlike
/// <see cref="SendGridSettings"/> — there is no sender address or HTML template here, only one template ID per
/// email purpose.
/// </summary>
public class NotifySettings
{
    /// <summary>
    /// Gets or sets the GOV.UK Notify API key. Follows the same convention as
    /// <see cref="SendGridSettings.EmailApiKey"/>: never committed as a real value (empty in appsettings.json and
    /// appsettings.Test.json, a placeholder in appsettings.Development.json), with the real value injected as an
    /// environment-specific App Service application setting.
    ///
    /// Which key type goes in which environment is a Notify-dashboard decision, not something this app enforces:
    /// Notify itself restricts a "Team and guest list" key to safelisted/team addresses regardless of what the
    /// caller sends, so every non-Production environment must be issued that key type, and Production the "Live"
    /// key. Pasting the wrong type in is a config mistake this app does not currently guard against.
    /// </summary>
    public string ApiKey { get; set; }

    public string ClientUrl { get; set; }

    // Invitation copy differs by role (BLUM-7568), so — unlike SubmissionConfirmationTemplateId and
    // NewSignInTemplateId — there is one template ID per role rather than one for the whole email purpose.
    public string DhscAdminInvitationTemplateId { get; set; }

    public string LaAdminInvitationTemplateId { get; set; }

    public string LaUserInvitationTemplateId { get; set; }

    public string SubmissionConfirmationTemplateId { get; set; }
    public string NewSignInTemplateId { get; set; }

    /// <summary>
    /// Gets or sets the template for the "your maturity assessment has been submitted" email to the submitting LA
    /// user (BLUM-7858). Empty in every committed file for the same reason as
    /// <see cref="CollectionClosedTemplateId"/>: the Notify template does not exist yet, and
    /// NotifyMaturityAssessmentConfirmationEmailService treats a missing id as a logged no-send. Set it in
    /// appsettings.Development.json and as the App Service setting Notify__MaturityAssessmentConfirmationTemplateId
    /// once DHSC has created the template — and re-check the personalisation keys against it at the same time.
    /// </summary>
    public string MaturityAssessmentConfirmationTemplateId { get; set; }

    /// <summary>
    /// Gets or sets the template for the "reporting window closed" email to DHSC Super Admins (BLUM-7719).
    /// Deliberately EMPTY in every committed file: the Notify template does not exist yet, and
    /// NotifyCollectionClosedEmailService treats a missing id as a logged no-send rather than an error. Set it in
    /// appsettings.Development.json and as the App Service setting Notify__CollectionClosedTemplateId once DHSC
    /// has created the template — and re-check the personalisation keys against it at the same time.
    /// </summary>
    public string CollectionClosedTemplateId { get; set; }

    /// <summary>
    /// Gets or sets the template for the BLUM-7571 deadline reminder sent to data providers who have not yet
    /// submitted. Empty in every committed file for the same reason as
    /// <see cref="CollectionClosedTemplateId"/>: the Notify template does not exist yet, and
    /// NotifyCollectionReminderEmailService treats a missing id as a logged no-send. Set it in
    /// appsettings.Development.json and as the App Service setting Notify__CollectionReminderTemplateId once
    /// DHSC has created the template — and re-check the personalisation keys against it at the same time.
    /// </summary>
    public string CollectionReminderTemplateId { get; set; }

    /// <summary>
    /// Gets or sets the template for the "reporting window closed" email to LA users of an organisation that did
    /// not submit by the deadline (BLUM-8155). A separate template from <see cref="CollectionClosedTemplateId"/>
    /// on purpose — that one is the DHSC-admin-facing closure notice sent immediately, this is the LA-facing
    /// chase sent a day later, to a different audience with a different personalisation contract: Name, Deadline,
    /// and Data collection (confirmed against the live template's own "Missing personalisation" 400 — the ticket's
    /// field list called the third one "Quarterly report name", but the template as built uses "Data collection").
    /// DHSC has now created the template, id a6e6e0f2-4c05-49b6-9164-e1f815ff3a53, set in appsettings.json and
    /// appsettings.Development.json. NotifyCollectionWindowClosedLaEmailService still treats a missing/empty id as
    /// a logged no-send rather than an error, which is what covers appsettings.Test.json (left empty, same
    /// convention as every other Notify template id there).
    /// </summary>
    public string CollectionWindowClosedLaTemplateId { get; set; }

    /// <summary>
    /// Gets or sets the template for the BLUM-7571 summary sent to DHSC Super Admins whenever a reminder goes
    /// out, for a quarterly-report collection. Same "empty until DHSC creates it" convention as
    /// <see cref="CollectionReminderTemplateId"/>; App Service setting Notify__CollectionReminderSummaryTemplateId.
    ///
    /// The template needs a branch for outstanding_count = 0: AC5 sends this summary whenever the trigger point
    /// is reached, including when every organisation has already submitted and no reminder went anywhere.
    ///
    /// Maturity-assessment collections use <see cref="CollectionReminderSummaryMaturityTemplateId"/> instead, a
    /// separate template rather than one shared template with a quarterly/maturity branch — GOV.UK Notify's
    /// ((condition??text)) conditional syntax cannot contain another placeholder, which ruled that out for a
    /// sentence that needs the reporting period dates inline.
    /// </summary>
    public string CollectionReminderSummaryTemplateId { get; set; }

    /// <summary>
    /// Gets or sets the template for the BLUM-7571 summary sent to DHSC Super Admins whenever a reminder goes
    /// out, for a maturity-assessment collection specifically. Same "empty until DHSC creates it" convention as
    /// <see cref="CollectionReminderTemplateId"/>; App Service setting
    /// Notify__CollectionReminderSummaryMaturityTemplateId. See <see cref="CollectionReminderSummaryTemplateId"/>
    /// for why this is a second template rather than a branch inside that one.
    /// </summary>
    public string CollectionReminderSummaryMaturityTemplateId { get; set; }

    /// <summary>
    /// Gets or sets the template for the "your reporting window is open" email to each data provider contact
    /// (BLUM-7721). Same "empty until DHSC creates it" convention as <see cref="CollectionReminderTemplateId"/>;
    /// App Service setting Notify__CollectionWindowOpenedTemplateId.
    /// </summary>
    public string CollectionWindowOpenedTemplateId { get; set; }

    /// <summary>
    /// Gets or sets the template for the BLUM-7721 window-opened summary to DHSC Super Admins, for a
    /// quarterly-report collection. Same convention again; App Service setting
    /// Notify__CollectionWindowOpenedSummaryTemplateId. The template needs a branch for assigned_count = 0 — a
    /// window can open with no organisations assigned to it yet.
    ///
    /// Maturity-assessment collections use <see cref="CollectionWindowOpenedSummaryMaturityTemplateId"/> instead,
    /// for the same reason <see cref="CollectionReminderSummaryTemplateId"/> has a maturity sibling rather than a
    /// branch inside one shared template.
    /// </summary>
    public string CollectionWindowOpenedSummaryTemplateId { get; set; }

    /// <summary>
    /// Gets or sets the template for the BLUM-7721/BLUM-8094 window-opened summary to DHSC Super Admins, for a
    /// maturity-assessment collection specifically (BLUM-8094). Same "empty until DHSC creates it" convention as
    /// <see cref="CollectionReminderTemplateId"/>; App Service setting
    /// Notify__CollectionWindowOpenedSummaryMaturityTemplateId. See
    /// <see cref="CollectionWindowOpenedSummaryTemplateId"/> for why this is a second template rather than a
    /// branch inside that one.
    /// </summary>
    public string CollectionWindowOpenedSummaryMaturityTemplateId { get; set; }

    /// <summary>
    /// Gets or sets the template for the "your temporary password" email sent when an admin enables or resets
    /// password sign-in for a user (BLUM-XXXX). Deliberately EMPTY in every committed file: the Notify template
    /// does not exist yet, and NotifyTemporaryPasswordEmailService treats a missing id as a logged no-send
    /// rather than an error. Set it in appsettings.Development.json and as the App Service setting
    /// Notify__TemporaryPasswordTemplateId once DHSC has created the template — and re-check the
    /// personalisation keys against it at the same time.
    /// </summary>
    public string TemporaryPasswordTemplateId { get; set; }
}

/// <summary>
/// Paces the background half of the BLUM-7719 closure notification.
///
/// A window usually closes because its deadline passed, which is not an event anything observes — see
/// ICollectionClosureNotifier. So a sweep has to go looking, and these two knobs bound it.
/// </summary>
public class CollectionNotificationSettings
{
    /// <summary>
    /// Gets or sets how often the closure sweep runs. 0 or less disables the timer; the startup sweep still runs,
    /// which on an App Service with always_on = false is what actually carries this feature (see
    /// CollectionClosureNotificationHostedService).
    /// </summary>
    public int ClosureSweepIntervalHours { get; set; } = 6;

    /// <summary>
    /// Gets or sets how far past its deadline a window may be and still be worth notifying about.
    ///
    /// This is the backstop behind the migration that stamps every already-closed window as notified on deploy.
    /// Anything that leaves collections closed with closure_notified_at NULL — a restore from backup, a data
    /// reset, a manual DELETE — would otherwise mail every admin once per historical window on the next sweep.
    /// </summary>
    public int MaxClosureNotificationAgeDays { get; set; } = 7;

    /// <summary>
    /// Gets or sets how often the BLUM-7571 reminder sweep runs. Same semantics as
    /// <see cref="ClosureSweepIntervalHours"/>: 0 or less disables the timer and leaves only the startup sweep.
    /// </summary>
    public int ReminderSweepIntervalHours { get; set; } = 6;

    /// <summary>
    /// Gets or sets how far back a reminder's fire date may be and still be worth sending.
    ///
    /// This is what lets a reminder configured for a day the service was unloaded still go out (App Service runs
    /// with always_on = false), and it is the backstop behind the migration that claims every already-passed
    /// trigger point on deploy. Anything that leaves collection_reminder_dispatches empty — a restore from
    /// backup, a data reset — would otherwise mail every outstanding organisation once per historical fire date.
    /// </summary>
    public int MaxReminderNotificationAgeDays { get; set; } = 7;

    /// <summary>
    /// Gets or sets how many reminder emails are in flight at once.
    ///
    /// A "submitted by all LAs" window fans out to ~153 organisations, so several hundred sends; done one at a
    /// time that is minutes, and against a degraded Notify (NotifyEmailServiceBase allows 15s per send) it is
    /// hours. Mirrors BulkImportSettings.MaxConcurrentUserCreations, and 1 restores fully sequential behaviour.
    /// </summary>
    public int MaxConcurrentReminderSends { get; set; } = 6;

    /// <summary>
    /// Gets or sets how long the MANUAL reminder fan-out may take before it is abandoned. Clamped into
    /// 15..200 seconds by the handler: the ceiling sits under Azure App Service's 230-second idle timeout, the
    /// same platform constant BulkImportSettings is sized against.
    /// </summary>
    public int ManualReminderBudgetSeconds { get; set; } = 120;

    /// <summary>
    /// Gets or sets how often the BLUM-8155 "window closed" LA-notification sweep runs. Same semantics as
    /// <see cref="ClosureSweepIntervalHours"/>: 0 or less disables the timer and leaves only the startup sweep.
    /// </summary>
    public int WindowClosedLaSweepIntervalHours { get; set; } = 6;

    /// <summary>
    /// Gets or sets how far past its deadline a window may be and still be worth LA-notifying about. Same
    /// backstop reasoning as <see cref="MaxClosureNotificationAgeDays"/> — the migration that ships alongside
    /// Collection.WindowClosedLaNotifiedAt stamps every already-closed window as notified on deploy; this is the
    /// second layer, against a restore-from-backup or data reset that clears the marker afterwards.
    /// </summary>
    public int MaxWindowClosedLaNotificationAgeDays { get; set; } = 7;
}

public enum EmailProvider
{
    SendGrid,
    Notify,
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
    /// Whether a session is refused when the request's IP address no longer matches the one it was minted on
    /// (BLUM-7848). Off by default: the User-Agent half of the binding still refuses a cookie replayed from a
    /// different browser, but an address change on its own — which production audit rows showed happening to
    /// users who had not moved, behind proxy pools and edge connection recycling — is let through and
    /// recorded as mfa.session.ip_changed instead of sending them back to the code-entry screen mid-form.
    /// Set to true to restore the strict BLUM-7238 binding.
    /// </summary>
    public bool BindSessionToIpAddress { get; set; }

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

    /// <summary>
    /// audit_logs rows one user's device-binding refusals may cause per hour (BLUM-8321). Every refusal still
    /// reaches telemetry and the log; past this they are counted onto the next admitted row instead. Higher
    /// than the per-actor caps on the security trails because a user whose address genuinely keeps changing
    /// needs several rows before the pattern shows. 0 means unlimited.
    /// </summary>
    public int MaxDeviceMismatchAuditRowsPerUserPerHour { get; set; } = 10;

    /// <summary>
    /// Ceiling across all users. Per hour, and at the same figure as the security trails, for the reason
    /// InputValidationSettings.MaxAuditRowsPerHour gives: audit_logs rows outlive the flood, so anything
    /// looser buries the genuine user.* and mfa.* trail. 0 means unlimited.
    /// </summary>
    public int MaxDeviceMismatchAuditRowsPerHour { get; set; } = 60;

    public bool BypassForTesting { get; set; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether <see cref="Infrastructure.Persistence.ConfigurePersistenceServices"/>
    /// persists the Data Protection key ring — which protects every TOTP secret via MfaEncryptionService — to
    /// Azure Blob Storage instead of the default ephemeral, per-process key ring. Defaults to false so an
    /// environment that already has AzureBlob:ConnectionString set (for unrelated blob usage) does not silently
    /// start persisting keys the moment this ships; it stays opt-in per environment, same convention as
    /// <see cref="DatabaseSettings.UseManagedIdentity"/>.
    ///
    /// Turning this on for the first time is a one-time cutover, not a config tweak: the blob container starts
    /// empty, so the first boot with this true mints a brand-new key with no relationship to whatever ephemeral
    /// key was protecting secrets a moment before. Every already-enrolled user's EncryptedSecret becomes
    /// permanently undecryptable at that instant. Flip this on only in the same release as a call to
    /// IDataResetService.ClearMfaDataAsync (via the /internal/mfa-data/clear endpoint), not on its own.
    /// </summary>
    public bool UseBlobKeyPersistence { get; set; } = false;
}

/// <summary>
/// Retention of audit_logs (BLUM-8750). Swept by AuditLogRetentionHostedService on the same schedule shape as
/// the MFA session sweep: once at startup, then every <see cref="RetentionSweepIntervalHours"/>.
/// </summary>
public class AuditSettings
{
    /// <summary>
    /// How long an audit row is kept, counted from OccurredAt, before it is deleted. 0 — the default — keeps
    /// every row for ever, so retention is switched on per environment once DHSC has agreed a period, never by
    /// a deploy.
    ///
    /// This is a retention period for personal data, not just housekeeping: Before/After carry emails and names
    /// (user.*), IP addresses (security.*, mfa.session.*). It also applies to every action alike, which has a
    /// visible consequence: the deleted-users and deleted-organisations screens are built from user.deleted /
    /// organisation.deleted rows, so a deletion older than this period drops off them.
    /// </summary>
    public int RetentionDays { get; set; } = 0;

    /// <summary>
    /// How often the sweep repeats once the app is running. 0 leaves only the sweep that runs at startup. See
    /// MfaSettings.SessionRetentionSweepIntervalHours for why the startup sweep matters under always_on = false.
    /// </summary>
    public int RetentionSweepIntervalHours { get; set; } = 24;

    /// <summary>
    /// How many rows one delete statement may remove. Bounds how long a single statement holds locks on a table
    /// every audited write appends to, not how much a sweep deletes overall.
    /// </summary>
    public int RetentionSweepBatchSize { get; set; } = 1000;
}