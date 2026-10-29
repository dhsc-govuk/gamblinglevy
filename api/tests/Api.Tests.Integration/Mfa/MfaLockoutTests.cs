using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.Common.Interfaces;
using Application.Common.Settings;
using Application.Mfa.Dtos;
using Domain.AuditLogs;
using Domain.Users;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OtpNet;
using Tests.Common;
using Tests.Data;
using Xunit;

namespace Api.Tests.Integration.Mfa;

/// <summary>
/// BLUM-7239 AC3 / CWE-770 — "more than five incorrect codes locks the session and requires re-auth".
///
/// A lockout already existed on /mfa/verify before this story. What did not was any of the four things that
/// made it a control rather than an inconvenience: it never released, it covered one of the five endpoints
/// that take a code, it left every established session running, and its thresholds were unreachable
/// constants. Each section below is one of those.
///
/// MaxAttemptsPerWindow is set to 0 for most of this class on purpose. The per-minute damper and the lockout
/// both trip at five in production, so with both live the damper always answers first and the lockout can
/// never be observed — the damper has its own section, which re-arms it.
/// </summary>
public class MfaLockoutTests : BaseIntegrationTest, IAsyncLifetime
{
    private const string ClientIp = "203.0.113.20";
    private const string TestUserAgent = "IntegrationTest/1.0";
    private const string MfaSessionCookieName = "mfa_session";

    private const int MaxFailedAttempts = 3;
    private const int LockoutMinutes = 30;

    private static readonly UserId User = AdminsData.MainAdmin.Id;

    private readonly ApplicationSettings settings;
    private readonly IMfaRateLimitService rateLimiter;

    public MfaLockoutTests(IntegrationTestWebFactory factory)
        : base(factory)
    {
        this.settings = factory.Services.GetRequiredService<ApplicationSettings>();
        this.rateLimiter = factory.Services.GetRequiredService<IMfaRateLimitService>();
    }

    public async Task InitializeAsync()
    {
        // The re-auth half of AC3 is enforced by MfaRequiredMiddleware, which the suite bypasses by default.
        // IntegrationTestWebFactory is an IClassFixture, so this settings instance belongs to this class
        // alone and flipping it cannot leak into a parallel one.
        this.settings.Mfa.BypassForTesting = false;
        this.settings.Mfa.MaxFailedAttempts = MaxFailedAttempts;
        this.settings.Mfa.LockoutMinutes = LockoutMinutes;
        this.settings.Mfa.MaxAttemptsPerWindow = 0;

        // The damper counts in a cache that outlives a test method — one host serves the class.
        this.rateLimiter.ResetAttempts(User);

        this.Context.MfaSessions.RemoveRange(this.Context.MfaSessions);
        this.Context.UserMfas.RemoveRange(this.Context.UserMfas);
        this.Context.AuditLogs.RemoveRange(this.Context.AuditLogs);
        this.Context.Users.RemoveRange(this.Context.Users);
        await this.SaveChangesAsync();

        await this.Context.Users.AddAsync(AdminsData.MainAdmin);
        await this.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await RejectedInserts.RestoreAsync(this.Context);

        this.settings.Mfa.BypassForTesting = true;
        this.settings.Mfa.MaxAttemptsPerWindow = 5;
        this.rateLimiter.ResetAttempts(User);

        this.Context.MfaSessions.RemoveRange(this.Context.MfaSessions);
        this.Context.UserMfas.RemoveRange(this.Context.UserMfas);
        this.Context.AuditLogs.RemoveRange(this.Context.AuditLogs);
        this.Context.Users.RemoveRange(this.Context.Users);
        await this.SaveChangesAsync();

        AuthenticateAsAdmin();
    }

    // ---- AC3: the lock -----------------------------------------------------------------------------

    [Fact]
    public async Task ShouldLockTheAccount_OnTheConfiguredNumberOfIncorrectCodes()
    {
        await this.CompleteSetupAsync();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < MaxFailedAttempts; i++)
        {
            statuses.Add((await this.VerifyAsync("000000")).StatusCode);
        }

        statuses.Take(MaxFailedAttempts - 1).Should().OnlyContain(status => status == HttpStatusCode.Unauthorized);
        statuses[^1].Should().Be(HttpStatusCode.TooManyRequests, "the attempt that reaches the threshold is the one that locks");

        var mfa = await this.MfaRecordAsync();
        mfa.LockedUntil.Should().NotBeNull();
        mfa.IsLockedOut(DateTime.UtcNow).Should().BeTrue();
    }

    [Fact]
    public async Task ShouldRefuseACorrectCode_WhileTheLockHolds()
    {
        var manualKey = await this.CompleteSetupAsync();
        await this.LockOutAsync();

        var response = await this.VerifyAsync(GenerateTotpCode(manualKey));

        response.StatusCode.Should().Be(
            HttpStatusCode.TooManyRequests,
            "a lock that a correct guess could walk through would only slow an attacker down by one code");
    }

    [Fact]
    public async Task ShouldLockTheRecoveryCodePathToo()
    {
        // A recovery code is guessable in exactly the way a TOTP code is, so leaving the escape hatch open
        // during a lockout would let the lockout simply be walked around.
        var (_, recoveryCodes) = await this.CompleteSetupWithCodesAsync();
        await this.LockOutAsync();

        var response = await this.VerifyRecoveryAsync(recoveryCodes[0]);

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task ShouldCountEveryCodeTakingEndpointAgainstOneBudget()
    {
        // The bypass this covers: a lock that only counted /mfa/verify would be defeated by guessing at
        // /mfa/disable instead.
        await this.CompleteSetupWithCodesAsync();

        (await this.VerifyAsync("000000")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await this.VerifyRecoveryAsync("AAAA-BBBB-CCCC")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var third = await this.PostCodeAsync("mfa/disable", "000000");

        third.StatusCode.Should().Be(HttpStatusCode.TooManyRequests, "three failures across three endpoints is still three failures");
        (await this.MfaRecordAsync()).IsLockedOut(DateTime.UtcNow).Should().BeTrue();
    }

    [Fact]
    public async Task ShouldNotCountACorrectCode()
    {
        var manualKey = await this.CompleteSetupAsync();

        await this.VerifyAsync("000000");
        (await this.VerifyAsync(GenerateTotpCode(manualKey))).IsSuccessStatusCode.Should().BeTrue();
        await this.VerifyAsync("000000");

        (await this.MfaRecordAsync()).FailedAttempts.Should().Be(1, "a success clears the counter rather than merely not adding to it");
    }

    // ---- AC3: "requiring re-auth" ------------------------------------------------------------------

    [Fact]
    public async Task ShouldRevokeEveryLiveSession_WhenTheLockIsApplied()
    {
        // This is what makes the lock mean re-authentication rather than just "no more guesses". Without it,
        // an attacker already sitting on an established session keeps it — the fail-open posture BLUM-7238
        // and BLUM-7241 exist to close.
        await this.CompleteSetupAsync();
        var sessionId = await this.SeedDeviceBoundSessionAsync(User);

        await this.LockOutAsync();

        var session = await this.Context.MfaSessions.AsNoTracking().FirstAsync(x => x.Id == new MfaSessionId(sessionId));
        session.IsRevoked.Should().BeTrue();

        // And the browser still holding that cookie is signed out on its next request, through machinery the
        // client already has: 401 SESSION_REVOKED is what decideAuthFailureAction turns into a sign-out.
        var replay = await this.SendAsync(HttpMethod.Get, "admin/users", sessionId);

        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ErrorCodeAsync(replay)).Should().Be("SESSION_REVOKED");
    }

    [Fact]
    public async Task ShouldStillLockAndRevoke_WhenTheFailuresCannotBeAudited()
    {
        // PR #87 review: the count once committed only if its audit row did, so while audit inserts were failing
        // every wrong code rolled its own count back — the lock and the revocation never came, and an attacker had
        // only the per-minute damper (switched off in this class) in their way. The rows are the record of the
        // control, not the control.
        await this.CompleteSetupAsync();
        var sessionId = await this.SeedDeviceBoundSessionAsync(User);
        await RejectedInserts.RejectAuditAsync(this.Context, AuditAction.MfaVerifyFailed);
        await RejectedInserts.RejectAuditAsync(this.Context, AuditAction.MfaLockedOut);

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < MaxFailedAttempts; i++)
        {
            statuses.Add((await this.VerifyAsync("000000")).StatusCode);
        }

        statuses.Take(MaxFailedAttempts - 1).Should().OnlyContain(status => status == HttpStatusCode.Unauthorized);
        statuses[^1].Should().Be(HttpStatusCode.TooManyRequests);

        var mfa = await this.MfaRecordAsync();
        mfa.FailedAttempts.Should().Be(MaxFailedAttempts);
        mfa.IsLockedOut(DateTime.UtcNow).Should().BeTrue();

        var session = await this.Context.MfaSessions.AsNoTracking().FirstAsync(x => x.Id == new MfaSessionId(sessionId));
        session.IsRevoked.Should().BeTrue();

        (await this.Context.AuditLogs.AsNoTracking().AnyAsync(x => x.Action == AuditAction.MfaVerifyFailed || x.Action == AuditAction.MfaLockedOut))
            .Should().BeFalse("the triggers refused every one of them, which is the premise of the test");
    }

    [Fact]
    public async Task ShouldRevokeOnce_NotOnEveryFurtherFailure()
    {
        await this.CompleteSetupAsync();
        await this.LockOutAsync();

        for (var i = 0; i < 4; i++)
        {
            await this.VerifyAsync("000000");
        }

        var lockouts = await this.AuditRowsAsync(AuditAction.MfaLockedOut);

        lockouts.Should().HaveCount(1, "a row per attempt during a lockout would let the attacker choose how many the service writes");
    }

    [Fact]
    public async Task ShouldKeepAnsweringLockedOut_WhenTheDamperIsAlsoLive()
    {
        // Deliberately does NOT set MaxAttemptsPerWindow to 0, unlike every other case in this class. That
        // setting exists so the lockout can be reached past the damper, and it is exactly what hid this bug:
        // in production both thresholds are five, so the damper's budget is spent at the instant the lock
        // applies, and because the handler charges the damper before it can know the account is locked, the
        // next attempt answered MFA_RATE_LIMITED. That envelope is not a session-ended reason, so the user
        // was not signed out, and the view told someone locked for thirty minutes to wait one.
        //
        // The loop is the point, and it is what the first version of this test missed. That fix cleared the
        // damper as the lock went on, which bought exactly one window's worth of correct answers: the
        // handlers charge the damper again on every retry, so somebody who kept retyping crossed the
        // threshold a second time and went straight back to MFA_RATE_LIMITED. Asserting only the next
        // attempt passed against that. The lock has to be what answers for as long as the lock lasts.
        this.settings.Mfa.MaxAttemptsPerWindow = MaxFailedAttempts;
        this.rateLimiter.ResetAttempts(User);

        await this.CompleteSetupAsync();
        await this.LockOutAsync();

        for (var attempt = 1; attempt <= (MaxFailedAttempts * 2) + 1; attempt++)
        {
            var response = await this.VerifyAsync("000000");

            response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
            (await ErrorCodeAsync(response)).Should().Be(
                "MFA_LOCKED_OUT",
                "the lock is the reason attempt {0} during it is refused, so it must be the reason the caller is given",
                attempt);
        }
    }

    // ---- AC3: getting back in ----------------------------------------------------------------------

    [Fact]
    public async Task ShouldAllowAFreshBudget_OnceTheLockExpires()
    {
        // The single most important assertion in this class. The counter used to survive its own lockout, so
        // the first wrong digit after the lock elapsed pushed it past the threshold again and re-locked
        // immediately — a permanent lockout, and not escapable by recovery code either, since that path
        // consults the same lock. An administrator's mfa reset was the only way back.
        await this.CompleteSetupAsync();
        await this.LockOutAsync();

        await this.ExpireLockAsync();

        var response = await this.VerifyAsync("000000");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "not 429 — the lock is over");

        var mfa = await this.MfaRecordAsync();
        mfa.FailedAttempts.Should().Be(1, "the counter restarted with the window rather than carrying on from the threshold");
        mfa.IsLockedOut(DateTime.UtcNow).Should().BeFalse();
    }

    [Fact]
    public async Task ShouldAcceptARecoveryCode_OnceTheLockExpires()
    {
        var (_, recoveryCodes) = await this.CompleteSetupWithCodesAsync();
        await this.LockOutAsync();
        await this.ExpireLockAsync();

        var response = await this.VerifyRecoveryAsync(recoveryCodes[0]);

        response.IsSuccessStatusCode.Should().BeTrue("the self-service escape hatch has to actually work once the lock is over");
    }

    // ---- AC1: the 429 contract ---------------------------------------------------------------------

    [Fact]
    public async Task ShouldSetRetryAfter_OnTheLockout()
    {
        await this.CompleteSetupAsync();

        var response = await this.LockOutAsync();

        var retryAfter = response.Headers.RetryAfter?.Delta?.TotalSeconds
            ?? throw new InvalidOperationException("Retry-After was missing or not in delta-seconds form.");

        retryAfter.Should().BeInRange(1, LockoutMinutes * 60);
    }

    [Fact]
    public async Task ShouldAnswerTheLockedOutEnvelope()
    {
        await this.CompleteSetupAsync();

        var response = await this.LockOutAsync();
        var body = await response.Content.ReadAsStringAsync();

        using var document = JsonDocument.Parse(body);

        document.RootElement.GetProperty("error").GetString().Should().Be(
            "MFA_LOCKED_OUT",
            "a client that could not tell this from the per-minute damper would tell a locked-out user to wait a few minutes");
        document.RootElement.GetProperty("lockedUntil").GetString().Should().NotBeNullOrEmpty();

        // The exception message interpolates the user's id, and this body is returned by a guessing endpoint.
        body.Should().NotContain(User.Value.ToString());
    }

    [Fact]
    public async Task ShouldNotDiscloseHowManyGuessesRemain()
    {
        // RateLimit-Remaining on a verification endpoint is a live read-out of how many attempts are left
        // before the lock trips — it tells an attacker exactly when to back off so it never does. The
        // business endpoints advertise their budget; these deliberately do not.
        await this.CompleteSetupAsync();

        var response = await this.VerifyAsync("000000");

        response.Headers.Contains("RateLimit-Limit").Should().BeFalse();
        response.Headers.Contains("RateLimit-Remaining").Should().BeFalse();
    }

    // ---- The per-minute damper ---------------------------------------------------------------------

    [Theory]
    [InlineData("mfa/verify")]
    [InlineData("mfa/verify/recovery")]
    [InlineData("mfa/setup/verify")]
    [InlineData("mfa/disable")]
    [InlineData("mfa/recovery-codes/regenerate")]
    public async Task ShouldDampenEveryCodeTakingEndpoint(string route)
    {
        // Each handler reserves before it does anything else, so this holds whatever the endpoint would
        // eventually have answered. That matters most for mfa/verify/recovery, which spends roughly two and
        // a half CPU-seconds on BCrypt per wrong guess and had no damper at all until this story.
        this.settings.Mfa.MaxAttemptsPerWindow = 2;
        this.rateLimiter.ResetAttempts(User);

        await this.PostCodeAsync(route, "000000");
        await this.PostCodeAsync(route, "000000");

        var third = await this.PostCodeAsync(route, "000000");

        third.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await ErrorCodeAsync(third)).Should().Be("MFA_RATE_LIMITED");
    }

    [Fact]
    public async Task ShouldSetRetryAfter_OnTheDamper()
    {
        this.settings.Mfa.MaxAttemptsPerWindow = 1;
        this.rateLimiter.ResetAttempts(User);

        await this.VerifyAsync("000000");
        var response = await this.VerifyAsync("000000");

        var retryAfter = response.Headers.RetryAfter?.Delta?.TotalSeconds
            ?? throw new InvalidOperationException("Retry-After was missing or not in delta-seconds form.");

        retryAfter.Should().BeInRange(1, this.settings.Mfa.RateLimitWindowSeconds);
    }

    // ---- The trail ---------------------------------------------------------------------------------

    [Fact]
    public async Task ShouldAuditEveryFailedRecoveryCode()
    {
        // This path wrote no audit row at all before BLUM-7239, so a brute force against the recovery codes
        // — each one a single-use bypass of the authenticator — was invisible while the same attack on TOTP
        // was recorded. AC3 is unverifiable on this path without it.
        await this.CompleteSetupWithCodesAsync();

        await this.VerifyRecoveryAsync("AAAA-BBBB-CCCC");
        await this.VerifyRecoveryAsync("DDDD-EEEE-FFFF");

        (await this.AuditRowsAsync(AuditAction.MfaRecoveryCodeFailed)).Should().HaveCount(2);
    }

    [Fact]
    public async Task ShouldRecordAFailedSetupVerification()
    {
        // The endpoint whose success hands the attacker the authenticator was the only one recording nothing
        // — no audit row, and no increment of the persistent counter — so a brute force against a pending
        // enrolment was both invisible and bounded only by an in-memory damper that resets on every unload.
        var setupResponse = await this.SendAsync(HttpMethod.Post, "mfa/setup", sessionId: null);
        var setup = await setupResponse.ToResponseModel<MfaSetupResponseDto>();
        setup.ManualEntryKey.Should().NotBeNullOrEmpty();

        await this.PostCodeAsync("mfa/setup/verify", "000000");
        await this.PostCodeAsync("mfa/setup/verify", "000000");

        (await this.MfaRecordAsync()).FailedAttempts.Should().Be(2);

        var rows = await this.AuditRowsAsync(AuditAction.MfaVerifyFailed);
        rows.Should().HaveCount(2);

        // The action says a code was wrong; the endpoint says where. Without the endpoint these rows are
        // indistinguishable from guessing at the sign-in screen, which points an administrator at the wrong
        // control.
        JsonDocument.Parse(rows[0].After!).RootElement.GetProperty("endpoint").GetString()
            .Should().Be("mfa/setup/verify");
    }

    [Fact]
    public async Task ShouldNameTheEndpointThatWasGuessedAt()
    {
        await this.CompleteSetupAsync();

        await this.PostCodeAsync("mfa/disable", "000000");

        var rows = await this.AuditRowsAsync(AuditAction.MfaVerifyFailed);

        rows.Should().HaveCount(1);
        JsonDocument.Parse(rows[0].After!).RootElement.GetProperty("endpoint").GetString()
            .Should().Be("mfa/disable", "the lockout row used to report its endpoint as the audit action itself");
    }

    [Fact]
    public async Task ShouldAuditTheLockout_WithTheCounterThatCausedIt()
    {
        await this.CompleteSetupAsync();
        await this.LockOutAsync();

        var rows = await this.AuditRowsAsync(AuditAction.MfaLockedOut);

        rows.Should().HaveCount(1);
        rows[0].TargetType.Should().Be(AuditTargetType.User);

        using var payload = JsonDocument.Parse(rows[0].After!);
        payload.RootElement.GetProperty("failedAttempts").GetInt32().Should().Be(MaxFailedAttempts);
    }

    private static string GenerateTotpCode(string base32Secret)
    {
        var totp = new Totp(Base32Encoding.ToBytes(base32Secret), step: 30, totpSize: 6);

        return totp.ComputeTotp();
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement.GetProperty("error").GetString();
    }

    private static string HashForBinding(string input) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(input)));

    /// <summary>Drives the account to its threshold and returns the response that applied the lock.</summary>
    private async Task<HttpResponseMessage> LockOutAsync()
    {
        HttpResponseMessage? last = null;

        for (var i = 0; i < MaxFailedAttempts; i++)
        {
            last = await this.VerifyAsync("000000");
        }

        return last!;
    }

    /// <summary>
    /// Back-dates the lock through the database. UserMfa exposes no setter for it, and adding a test-only one
    /// would be a hole in the aggregate purely to make an assertion convenient — the same reasoning
    /// MfaSessionTimeoutTests gives for its own back-dating.
    /// </summary>
    private async Task ExpireLockAsync()
    {
        await this.Context.UserMfas
            .Where(x => x.UserId == User)
            .ExecuteUpdateAsync(x => x.SetProperty(m => m.LockedUntil, DateTime.UtcNow.AddMinutes(-1)));

        this.Context.ChangeTracker.Clear();
    }

    private Task<HttpResponseMessage> VerifyAsync(string code) => this.PostCodeAsync("mfa/verify", code);

    private Task<HttpResponseMessage> VerifyRecoveryAsync(string code) => this.PostCodeAsync("mfa/verify/recovery", code);

    private async Task<HttpResponseMessage> PostCodeAsync(string route, string code)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, route)
        {
            Content = JsonContent.Create(new MfaVerifyRequestDto(code)),
        };
        request.Headers.Add("X-Azure-ClientIP", ClientIp);

        return await this.Client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string route, Guid? sessionId)
    {
        var request = new HttpRequestMessage(method, route);
        request.Headers.Add("X-Azure-ClientIP", ClientIp);

        if (sessionId is not null)
        {
            // The cookie is Secure and the test client's base address is http://localhost, so the handler's
            // CookieContainer will never echo it back — set the header explicitly instead.
            request.Headers.Add("Cookie", $"{MfaSessionCookieName}={sessionId}");
        }

        return await this.Client.SendAsync(request);
    }

    private async Task<string> CompleteSetupAsync()
    {
        var (manualKey, _) = await this.CompleteSetupWithCodesAsync();

        return manualKey;
    }

    private async Task<(string ManualKey, List<string> RecoveryCodes)> CompleteSetupWithCodesAsync()
    {
        var setupResponse = await this.SendAsync(HttpMethod.Post, "mfa/setup", sessionId: null);
        var setup = await setupResponse.ToResponseModel<MfaSetupResponseDto>();

        var verifyResponse = await this.PostCodeAsync("mfa/setup/verify", GenerateTotpCode(setup.ManualEntryKey));
        verifyResponse.IsSuccessStatusCode.Should().BeTrue();

        var complete = await verifyResponse.ToResponseModel<MfaSetupCompleteResponseDto>();

        return (setup.ManualEntryKey, complete.RecoveryCodes);
    }

    /// <summary>
    /// A session bound to the same device this class's requests come from, so validation reaches the
    /// revocation check rather than stopping at the device binding. The hashes must match
    /// MfaSessionService.ComputeHash — base64 of the SHA-256, not hex.
    /// </summary>
    private async Task<Guid> SeedDeviceBoundSessionAsync(UserId userId)
    {
        var session = MfaSession.New(
            MfaSessionId.New(),
            userId,
            ipAddressHash: HashForBinding(ClientIp),
            userAgentHash: HashForBinding(TestUserAgent),
            ipAddress: ClientIp,
            deviceLabel: "Chrome on Windows",
            utcNow: DateTime.UtcNow);

        await this.Context.MfaSessions.AddAsync(session);
        await this.SaveChangesAsync();

        return session.Id.Value;
    }

    private async Task<UserMfa> MfaRecordAsync()
    {
        return await this.Context.UserMfas.AsNoTracking().FirstAsync(x => x.UserId == User);
    }

    private async Task<List<AuditLog>> AuditRowsAsync(string action)
    {
        return await this.Context.AuditLogs.AsNoTracking().Where(x => x.Action == action).ToListAsync();
    }
}