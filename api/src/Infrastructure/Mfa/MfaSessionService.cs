using System.Security.Cryptography;
using System.Text;
using Application.Common.Devices;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.Common.Settings;
using Application.Common.Telemetry;
using Domain.AuditLogs;
using Domain.Users;
using LanguageExt.UnsafeValueAccess;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Mfa;

public class MfaSessionService(
    IMfaSessionRepository sessionRepository,
    IAuditLogRepository auditLogRepository,
    IUserQueries userQueries,
    Lazy<INewSignInEmailService> newSignInEmailService,
    IMfaDeviceMismatchRecorder deviceMismatchRecorder,
    IMemoryCache memoryCache,
    TelemetryClient telemetryClient,
    ILogger<MfaSessionService> logger,
    IDateTimeProvider dateTimeProvider,
    ApplicationSettings settings,
    ITransactionRunner transactionRunner)
    : IMfaSessionService
{
    // How stale LastActivityAt has to be before a validated request writes it back. See ValidateSessionAsync.
    private const int ActivityWriteThresholdSeconds = 60;

    // Namespaces the per-session "addresses already recorded" entries. See RecordIpTransitionAsync.
    private const string SeenAddressCacheScope = "mfa_session_seen_ip";

    public string HashIpAddress(string ipAddress) => ComputeHash(ipAddress);

    public string HashUserAgent(string userAgent) => ComputeHash(userAgent);

    private static string ComputeHash(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToBase64String(bytes);
    }

    public async Task<MfaSession> CreateSessionAsync(
        UserId userId,
        string ipAddress,
        string userAgent)
    {
        // The hash is taken over the address exactly as the client stated it, and the normalised form is only
        // what gets stored and shown. Canonicalising before hashing would change the binding for any address
        // that does not round-trip byte-identically, and every session already in the database was bound with
        // the unaltered string — so on deploy those users would all be told DeviceMismatch and thrown back to
        // the code-entry screen mid-task. Normalising here instead also covers every source of the address,
        // including the socket-address fallback, rather than only the two header paths.
        var session = MfaSession.New(
            MfaSessionId.New(),
            userId,
            HashIpAddress(ipAddress),
            HashUserAgent(userAgent),
            IpAddressNormaliser.Normalise(ipAddress),
            DeviceLabelResolver.Resolve(userAgent),
            dateTimeProvider.GetUtcNow(),
            settings.Mfa.SessionExpiryHours);

        // The session and its audit row commit together (BLUM-8748). Only those two: the housekeeping below
        // swallows its own failures, and a failed statement inside a PostgreSQL transaction would sink the commit.
        await transactionRunner.RunAsync(
            async _ =>
            {
                await sessionRepository.AddAsync(session);

                // The address and device deliberately stay OUT of this row. audit_logs has no retention sweep, so
                // copying them here would keep personal data indefinitely that mfa_sessions holds for only
                // SessionRetentionDays — and show it to every DHSC admin. The row's target is the session id,
                // which leads to them for as long as the retention period allows.
                await auditLogRepository.AppendAsync(
                    AuditLog.Create(userId, AuditTargetType.MfaSession, session.Id.Value, AuditAction.MfaSessionCreated, before: null, after: null),
                    CancellationToken.None);
            },
            CancellationToken.None);

        // The sign-in's session details go to the log and to telemetry instead (BLUM-7848), both of which have
        // their own retention, so a later ip_changed or device_mismatch event can be read against where the
        // user actually signed in from. Both values are already normalised / resolved labels, never raw headers.
        telemetryClient.TrackEventIfEnabled(
            "mfa_session_created",
            new Dictionary<string, string>
            {
                ["sessionId"] = session.Id.Value.ToString(),
                ["userId"] = userId.Value.ToString(),
                ["ip"] = session.IpAddress,
                ["device"] = session.DeviceLabel,
                ["expiresAt"] = session.ExpiresAt.ToString("O"),
            });

        logger.LogInformation(
            "MFA session {SessionId} created for user {UserId}: ip {IpAddress}, device {DeviceLabel}, expires {ExpiresAt:O}",
            session.Id.Value,
            userId.Value,
            session.IpAddress,
            session.DeviceLabel,
            session.ExpiresAt);

        // Read AFTER the insert, deliberately. Reading first meant two simultaneous sign-ins each judged a set
        // that was missing the other's row, so both concluded they were inside the cap and the user ended up
        // over it — under a limit of 1, an attacker's session surviving alongside the victim's, which is the
        // exact state this control exists to prevent. Counting a set that already contains this sign-in is
        // also simply the right question: "how many now?", not "how many a moment ago?".
        // null means the read itself failed, which is NOT the same as "this user has no other sessions" — the
        // two used to be indistinguishable, so a database blip silently switched off both controls at once.
        var activeSessions = await ReadActiveSessionsForControlsAsync(userId);
        var otherSessions = activeSessions?.Where(x => x.Id != session.Id).ToList();

        await EnforceConcurrentSessionLimitAsync(activeSessions, session);
        await NotifyIfAnotherSessionIsLiveAsync(userId, otherSessions, session);

        // Retention is deliberately NOT swept here. It used to be, scoped to whoever was signing in, which
        // meant the records of people who had stopped using the service were the only ones never deleted —
        // exactly the data with the least justification for being kept. MfaSessionRetentionHostedService owns
        // it now, for everybody, and sign-in is one step lighter for it.
        return session;
    }

    /// <summary>
    /// The set the controls below are judged against, or <c>null</c> when it could not be read.
    ///
    /// Null rather than an empty list, and the distinction is the point. An empty list is a fact — this user
    /// has no other sessions — and both controls correctly do nothing with it. A failed read is not that
    /// fact, and returning one for the other switched off the concurrency cap and the sign-in alert together,
    /// silently, on any transient database error. The callers now decide for themselves what "unknown" means:
    /// the cap can only stand down, the notification errs towards sending.
    ///
    /// Still does not throw. Everything after the insert is housekeeping on a sign-in that has already
    /// happened — the row and its audit entry are committed — so failing the request here would hand the user
    /// a failed verification for a session that exists, and each retry would strand another live-but-
    /// unreachable row that still counts towards the cap.
    /// </summary>
    private async Task<List<MfaSession>?> ReadActiveSessionsForControlsAsync(UserId userId)
    {
        try
        {
            return await sessionRepository.GetActiveByUserIdAsync(userId);
        }
        catch (Exception exception)
        {
            // Error, not Warning: a security control was skipped, and that should be visible in the logs as
            // something to investigate rather than as routine noise.
            logger.LogError(
                exception,
                "Could not read active sessions for user {UserId} after sign-in; the concurrency cap is skipped for this sign-in.",
                userId.Value);

            return null;
        }
    }

    /// <summary>
    /// Holds the user to MfaSettings.MaxConcurrentSessions by revoking their least recently active sessions
    /// (BLUM-7241 / CWE-613). A limit of 1 is therefore a strict single-session policy an environment can
    /// configure without any code change.
    /// </summary>
    /// <param name="activeSessions">
    /// Every live session including the one just created, or null if they could not be read — in which case
    /// the cap stands down, because there is nothing it could act on without guessing.
    /// </param>
    private async Task EnforceConcurrentSessionLimitAsync(List<MfaSession>? activeSessions, MfaSession newSession)
    {
        var limit = settings.Mfa.MaxConcurrentSessions;
        if (limit <= 0 || activeSessions is null || activeSessions.Count <= limit)
        {
            return;
        }

        try
        {
            await RevokeSurplusSessionsAsync(activeSessions, newSession, limit);
        }
        catch (Exception exception)
        {
            // Guarded for the same reason the read above is: the sign-in is already committed. Refusing it
            // now would not bring the user back under the cap — it would leave the new session live and
            // unreachable, and invite a retry that creates another one. Staying over the cap until the next
            // sign-in re-runs this is the lesser failure, and it self-corrects.
            logger.LogWarning(
                exception,
                "Could not enforce the concurrent session limit for user {UserId}; they remain above it until their next sign-in.",
                newSession.UserId.Value);
        }
    }

    private async Task RevokeSurplusSessionsAsync(List<MfaSession> activeSessions, MfaSession newSession, int limit)
    {
        // The new session is pinned to the front rather than assumed to be there. Relying on its
        // LastActivityAt being the newest looked safe — it is stamped "now" — but the idle-window slide can
        // move another session's forward in the milliseconds between the insert and the read, and then
        // Skip(limit) selected the NEW session as surplus, the self-exclusion below dropped it, and nothing
        // was evicted at all: the user stayed over the cap with no audit entry to show for it.
        //
        // Pinning it also states the intent directly. Whoever is at the keyboard just proved possession of
        // their authenticator, so theirs is the one session there is positive evidence for; the ones evicted
        // are the least recently active of the rest.
        var surplus = activeSessions
            .OrderByDescending(x => x.Id == newSession.Id)
            .ThenByDescending(x => x.LastActivityAt)
            .Skip(limit)
            .ToList();

        foreach (var session in surplus)
        {
            // Conditional, so two sign-ins racing to evict the same session produce one revocation and one
            // audit line rather than two — and so a row another request already revoked cannot throw a
            // concurrency exception out of the sign-in path.
            // Each eviction commits with its audit row (BLUM-8748).
            await transactionRunner.RunAsync(
                async _ =>
                {
                    var revokedRows = await sessionRepository.TryRevokeSessionAsync(session.Id);
                    if (revokedRows == 0)
                    {
                        return;
                    }

                    await auditLogRepository.AppendAsync(
                        AuditLog.Create(session.UserId, AuditTargetType.MfaSession, session.Id.Value, AuditAction.MfaSessionEvicted, before: null, after: null),
                        CancellationToken.None);
                },
                CancellationToken.None);
        }
    }

    /// <summary>
    /// Emails the user when a session starts while another of theirs is already live (BLUM-7241 AC1).
    ///
    /// There is deliberately NO test for whether this looks like a device they already use, and both of the
    /// obvious ones were tried and removed. Matching on IP and User-Agent together emailed people about their
    /// own laptop every time it moved between networks, and an alert that cries wolf is one they learn to
    /// delete unread. Matching on User-Agent alone was worse: the header is set by whoever makes the request,
    /// so an attacker holding stolen credentials could copy the victim's User-Agent and switch the alert off
    /// entirely — the one out-of-band signal this whole remediation exists to produce, suppressed by a
    /// one-line change in their HTTP client.
    ///
    /// Anything derived from the request can be forged, so nothing derived from the request decides whether
    /// the user is told. The condition is only "was somebody already signed in as you" — which the attacker
    /// cannot influence. The cost is an email when a user re-verifies on a second device of their own, about
    /// a sign-in they just performed; the message says so, and it names the device, IP and time so they can
    /// tell at a glance.
    ///
    /// Entirely best-effort. The session exists and is usable by the time this runs, so nothing here may
    /// throw past the caller — being unable to warn somebody is not a reason to refuse them their sign-in.
    /// </summary>
    /// <param name="otherSessions">
    /// The user's other live sessions, or null if they could not be read. Null errs towards sending: not
    /// knowing whether somebody else is signed in is not a reason to stay quiet.
    /// </param>
    private async Task NotifyIfAnotherSessionIsLiveAsync(
        UserId userId,
        List<MfaSession>? otherSessions,
        MfaSession newSession)
    {
        if (otherSessions is { Count: 0 })
        {
            return;
        }

        try
        {
            var user = await userQueries.FindById(userId);
            if (user.IsNone)
            {
                logger.LogWarning(
                    "No user record for {UserId} while notifying a new sign-in; notification not sent.",
                    userId.Value);
                return;
            }

            var recipient = user.ValueUnsafe();

            // .Value is the first thing that builds the SendGrid transport — deliberately here, on the rare
            // sign-in that actually warrants an email, rather than on every request that passes the MFA gate.
            var result = await newSignInEmailService.Value.SendNewSignInAsync(
                recipient.Email,
                new NewSignInNotification(
                    recipient.Name.ToString(),
                    newSession.DeviceLabel,
                    newSession.IpAddress,
                    newSession.CreatedAt),
                CancellationToken.None);

            if (result.IsLeft)
            {
                result.IfLeft(exception => logger.LogWarning(
                    exception,
                    "New sign-in notification failed for user {UserId}; the sign-in itself is unaffected.",
                    userId.Value));
                return;
            }

            // Only written once SendGrid has accepted the message, so the trail records notifications that
            // were actually delivered rather than ones that were merely attempted.
            await auditLogRepository.AppendAsync(
                AuditLog.Create(userId, AuditTargetType.MfaSession, newSession.Id.Value, AuditAction.MfaSessionNotified, before: null, after: null),
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Unexpected failure notifying user {UserId} of a new sign-in; the sign-in itself is unaffected.",
                userId.Value);
        }
    }

    public async Task<MfaSessionStatus> ValidateSessionAsync(
        MfaSessionId sessionId,
        UserId userId,
        string ipAddress,
        string userAgent)
    {
        var session = await sessionRepository.GetByIdAsync(sessionId);
        if (session == null)
        {
            return MfaSessionStatus.NotFound;
        }

        // Ownership comes first, before the session is judged at all. mfa_session is a persistent cookie, so
        // on a shared browser the next person to sign in arrives carrying the previous user's cookie; acting
        // on it would destroy their session, audit it against them, and tell the new arrival they had been
        // timed out seconds after signing in. A cookie that is not ours is simply not ours.
        if (session.UserId != userId)
        {
            return MfaSessionStatus.NotOwned;
        }

        var now = dateTimeProvider.GetUtcNow();
        var ipAddressHash = HashIpAddress(ipAddress);
        var userAgentHash = HashUserAgent(userAgent);
        var status = session.Evaluate(
            ipAddressHash,
            userAgentHash,
            now,
            settings.Mfa.IdleTimeoutMinutes,
            settings.Mfa.BindSessionToIpAddress);

        // A session already destroyed by a timeout keeps reporting that timeout. Without this, only the one
        // request that happened to detect it gets the SESSION_* answer and every other request in the same
        // burst is told MFA_REQUIRED — which the client answers by sending the user to re-enter a code,
        // racing the sign-out and letting them back in without re-authenticating.
        if (status == MfaSessionStatus.Revoked && session.EndedReason is not null)
        {
            return session.EndedReason == MfaSessionEndReason.Expired
                ? MfaSessionStatus.Expired
                : MfaSessionStatus.IdleTimeout;
        }

        // BLUM-7238 / CWE-613 requires expiry to destroy the record, not merely stop honouring it.
        if (status is MfaSessionStatus.Expired or MfaSessionStatus.IdleTimeout)
        {
            var reason = status == MfaSessionStatus.Expired
                ? MfaSessionEndReason.Expired
                : MfaSessionEndReason.IdleTimeout;

            // The revocation commits with its audit row (BLUM-8748).
            await transactionRunner.RunAsync(
                async _ =>
                {
                    var revokedRows = await sessionRepository.TryRevokeTimedOutSessionAsync(sessionId, reason);

                    // Zero rows means a concurrent request destroyed the same session first. It already wrote the
                    // audit entry, so staying quiet here is what keeps one timeout worth one log line.
                    if (revokedRows > 0)
                    {
                        var action = status == MfaSessionStatus.Expired
                            ? AuditAction.MfaSessionExpired
                            : AuditAction.MfaSessionIdleTimeout;

                        await auditLogRepository.AppendAsync(
                            AuditLog.Create(session.UserId, AuditTargetType.MfaSession, session.Id.Value, action, before: null, after: null),
                            CancellationToken.None);
                    }
                },
                CancellationToken.None);

            return status;
        }

        // The refusal itself is unchanged; this only records it. It used to leave no trace at all, which is
        // how DAC users could be sent back to the code-entry screen every few minutes with nothing server-side
        // able to say whether their address or their browser had moved (BLUM-8321).
        if (status == MfaSessionStatus.DeviceMismatch)
        {
            await deviceMismatchRecorder.RecordAsync(DescribeBindingChange(session, ipAddress, ipAddressHash, userAgent, userAgentHash, now));
            return status;
        }

        if (status != MfaSessionStatus.Valid)
        {
            return status;
        }

        // Valid from somewhere other than where the session was last seen means the IP half of the binding
        // is switched off and the address moved (BLUM-7848). The request goes through; this keeps the move
        // visible — on the trail and on the user's own sessions list.
        if (session.LatestIpAddressHash() != ipAddressHash)
        {
            await RecordIpTransitionAsync(session, ipAddress, ipAddressHash, userAgent, userAgentHash, now);
        }

        // Slide the idle window. This runs inside the gate, so every authenticated request would otherwise
        // become a row UPDATE; the threshold keeps it to at most one write per minute per session. The cost
        // is that the effective idle window is IdleTimeoutMinutes plus up to a minute, which is well inside
        // the 30-minute requirement.
        if (now - session.LastActivityAt >= TimeSpan.FromSeconds(ActivityWriteThresholdSeconds))
        {
            await sessionRepository.TryUpdateLastActivityAsync(sessionId, now);
        }

        return MfaSessionStatus.Valid;
    }

    /// <summary>
    /// Records a tolerated address move once per transition, not once per request (BLUM-7848 review).
    ///
    /// Comparing every request against the sign-in address re-fired ip_changed for the rest of an 8-hour
    /// session after a single office-to-home move, so the user's hourly audit budget went on identical rows —
    /// and a replay from a third address that hour was the one suppressed. Comparing against the last-seen
    /// address, moved by a conditional update, makes one move worth one row across every instance.
    ///
    /// The in-process set of addresses already recorded for this session covers the other shape: a proxy
    /// pool that alternates between two addresses on every request is a transition each time. Remembering
    /// what this session has already been seen on means each address is recorded once, and the repeat
    /// requests cost a cache lookup — no UPDATE, no telemetry, no throttle reservation. A relative TTL only,
    /// as FixedWindowCounter explains; the session cannot outlive SessionExpiryHours anyway.
    ///
    /// Best-effort throughout: the request is already allowed, and failing to record the move must not fail it.
    /// </summary>
    private async Task RecordIpTransitionAsync(
        MfaSession session,
        string ipAddress,
        string ipAddressHash,
        string userAgent,
        string userAgentHash,
        DateTime now)
    {
        var cacheKey = (SeenAddressCacheScope, session.Id.Value, ipAddressHash);
        if (memoryCache.TryGetValue(cacheKey, out _))
        {
            return;
        }

        try
        {
            memoryCache.Set(cacheKey, true, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(Math.Max(1, settings.Mfa.SessionExpiryHours)),
                Size = 1,
            });

            var changed = await sessionRepository.TryUpdateLastSeenIpAsync(
                session.Id,
                session.LastSeenIpAddressHash,
                ipAddressHash,
                IpAddressNormaliser.Normalise(ipAddress));

            // Zero rows: a concurrent request moved it first and recorded the transition itself.
            if (changed == 0)
            {
                return;
            }

            // "Recorded" is where the session was last seen, so Before shows the address it moved from.
            var change = DescribeBindingChange(session, ipAddress, ipAddressHash, userAgent, userAgentHash, now) with
            {
                RecordedIpAddress = session.LatestIpAddress(),
            };

            await deviceMismatchRecorder.RecordToleratedIpChangeAsync(change);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not record an IP change for MFA session {SessionId}; the request is unaffected.", session.Id.Value);
        }
    }

    private static MfaDeviceMismatch DescribeBindingChange(
        MfaSession session,
        string ipAddress,
        string ipAddressHash,
        string userAgent,
        string userAgentHash,
        DateTime now) =>
        new(
            session.Id,
            session.UserId,
            session.IpAddress,
            IpAddressNormaliser.Normalise(ipAddress),
            session.DeviceLabel,
            DeviceLabelResolver.Resolve(userAgent),
            IpAddressMatches: session.IpAddressHash == ipAddressHash,
            UserAgentMatches: session.UserAgentHash == userAgentHash,
            SessionAgeSeconds: (long)(now - session.CreatedAt).TotalSeconds,
            IdleSeconds: (long)(now - session.LastActivityAt).TotalSeconds);

    public async Task<MfaSessionRevocationResult> RevokeSessionAsync(MfaSessionId sessionId, UserId userId)
    {
        // Deliberately not ValidateSessionAsync: that enforces the IP/User-Agent device binding, and a
        // logout must still destroy the session when the user's IP changed part-way through it.
        var session = await sessionRepository.GetByIdAsync(sessionId);
        if (session == null)
        {
            return MfaSessionRevocationResult.NotFound;
        }

        if (session.UserId != userId)
        {
            return MfaSessionRevocationResult.NotOwned;
        }

        session.Revoke();

        // The revocation commits with its audit row (BLUM-8748).
        await transactionRunner.RunAsync(
            async _ =>
            {
                await sessionRepository.UpdateAsync(session);

                await auditLogRepository.AppendAsync(
                    AuditLog.Create(session.UserId, AuditTargetType.MfaSession, session.Id.Value, AuditAction.MfaSessionRevoked, before: null, after: null),
                    CancellationToken.None);
            },
            CancellationToken.None);

        return MfaSessionRevocationResult.Revoked;
    }

    public async Task RevokeAllUserSessionsAsync(UserId userId)
    {
        // Every revocation and its audit row in one transaction (BLUM-8748) — or in the caller's, which this joins
        // when one is open: a deactivation, an MFA reset or a lockout revokes as part of its own change.
        await transactionRunner.RunAsync(
            async _ =>
            {
                var sessions = await sessionRepository.GetByUserIdAsync(userId);
                foreach (var session in sessions)
                {
                    session.Revoke();
                    await sessionRepository.UpdateAsync(session);

                    await auditLogRepository.AppendAsync(
                        AuditLog.Create(userId, AuditTargetType.MfaSession, session.Id.Value, AuditAction.MfaSessionRevoked, before: null, after: null),
                        CancellationToken.None);
                }
            },
            CancellationToken.None);
    }
}