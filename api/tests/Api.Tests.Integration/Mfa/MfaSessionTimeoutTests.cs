using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Application.Common.Devices;
using Application.Common.Settings;
using Application.Mfa.Dtos;
using Domain.AuditLogs;
using Domain.Users;
using Domain.ValueObjects;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OtpNet;
using Tests.Common;
using Tests.Data;
using Xunit;

namespace Api.Tests.Integration.Mfa;

/// <summary>
/// BLUM-7238 / CWE-613 — a session must be bounded server-side by an inactivity window and an absolute
/// lifetime, and hitting either must destroy the record rather than just refuse the request.
///
/// Like MfaSessionRevocationTests this class runs with the MFA gate switched ON, because the behaviour under
/// test lives entirely inside MfaRequiredMiddleware, which IntegrationTestWebFactory otherwise short-circuits
/// via ApplicationSettings.Mfa.BypassForTesting. BaseIntegrationTest is IClassFixture-scoped, so the settings
/// singleton belongs to this class alone and flipping it cannot leak into a parallel class.
/// </summary>
public class MfaSessionTimeoutTests : BaseIntegrationTest, IAsyncLifetime
{
    // Any route outside the middleware's "/mfa/", "/swagger", "/health" exemptions will do; this one is
    // admin-only, takes no route parameters, and the default test identity is a platform admin.
    private const string ProtectedRoute = "admin/users";
    private const string MfaSessionCookieName = "mfa_session";

    // The session binds to a SHA-256 of the User-Agent (and, with MfaSettings.BindSessionToIpAddress on, the
    // client IP) and compares them for exact equality, so every request here presents the same pair unless
    // it is meant to miss. The base class already fixes the User-Agent;
    // X-Azure-ClientIP is the first source MfaRequiredMiddleware checks.
    private const string ClientIp = "203.0.113.10";

    // Mirrors the User-Agent BaseIntegrationTest fixes on the shared HttpClient.
    private const string TestUserAgent = "IntegrationTest/1.0";

    // A second address and browser, for the requests that are meant to miss the binding (BLUM-8321).
    private const string OtherClientIp = "198.51.100.20";
    private const string ThirdClientIp = "192.0.2.30";
    private const string OtherUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36";

    private readonly ApplicationSettings _settings;

    public MfaSessionTimeoutTests(IntegrationTestWebFactory factory)
        : base(factory)
    {
        _settings = factory.Services.GetRequiredService<ApplicationSettings>();
    }

    [Fact]
    public async Task ShouldAllowRequest_WhenWithinIdleWindow()
    {
        var sessionId = await CompleteMfaSetupAsync();

        await SetLastActivityAsync(sessionId, DateTime.UtcNow.AddMinutes(-_settings.Mfa.IdleTimeoutMinutes + 5));

        var response = await SendAsync(HttpMethod.Get, ProtectedRoute, sessionId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetSessionAsync(sessionId)).IsRevoked.Should().BeFalse();
    }

    [Fact]
    public async Task ShouldRejectAndDestroySession_WhenIdleExceedsConfiguredWindow()
    {
        var sessionId = await CompleteMfaSetupAsync();

        await SetLastActivityAsync(sessionId, DateTime.UtcNow.AddMinutes(-_settings.Mfa.IdleTimeoutMinutes - 1));

        var response = await SendAsync(HttpMethod.Get, ProtectedRoute, sessionId);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync()).Should().Contain("SESSION_IDLE_TIMEOUT");

        // AC4: the record is destroyed, not merely refused for this one request.
        (await GetSessionAsync(sessionId)).IsRevoked.Should().BeTrue();
        await AssertAuditedAsync(sessionId, AuditAction.MfaSessionIdleTimeout);
    }

    [Fact]
    public async Task ShouldSlideIdleWindow_WhenUserIsActive()
    {
        var sessionId = await CompleteMfaSetupAsync();

        var staleActivity = DateTime.UtcNow.AddMinutes(-_settings.Mfa.IdleTimeoutMinutes + 5);
        await SetLastActivityAsync(sessionId, staleActivity);

        var response = await SendAsync(HttpMethod.Get, ProtectedRoute, sessionId);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // The request itself is the activity, so the window must have moved forward — otherwise a user
        // working continuously would still be signed out 30 minutes after they first loaded a page.
        (await GetSessionAsync(sessionId)).LastActivityAt.Should().BeAfter(staleActivity);
    }

    [Fact]
    public async Task ShouldRejectAndDestroySession_WhenAbsoluteExpiryReached()
    {
        var sessionId = await CompleteMfaSetupAsync();

        // AC2: this user is continuously active — LastActivityAt is left untouched — so only the absolute
        // cap can end the session.
        await SetExpiresAtAsync(sessionId, DateTime.UtcNow.AddMinutes(-1));

        var response = await SendAsync(HttpMethod.Get, ProtectedRoute, sessionId);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync()).Should().Contain("SESSION_EXPIRED");

        var session = await GetSessionAsync(sessionId);
        session.IsRevoked.Should().BeTrue();
        session.LastActivityAt.Should().BeAfter(DateTime.UtcNow.AddMinutes(-1));
        await AssertAuditedAsync(sessionId, AuditAction.MfaSessionExpired);
    }

    [Fact]
    public async Task ShouldRejectReplayedCookie_AfterIdleTimeout()
    {
        var sessionId = await CompleteMfaSetupAsync();

        await SetLastActivityAsync(sessionId, DateTime.UtcNow.AddMinutes(-_settings.Mfa.IdleTimeoutMinutes - 1));

        var timedOut = await SendAsync(HttpMethod.Get, ProtectedRoute, sessionId);
        timedOut.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // The verdict has to outlive the request that detected it. If the replay came back as a plain
        // MFA_REQUIRED the client would answer it by sending the user to re-enter a code — racing the
        // sign-out and letting them back in with no CIAM re-authentication at all. Every request against a
        // timed-out session must keep saying "timed out", not just the first one to notice.
        var replay = await SendAsync(HttpMethod.Get, ProtectedRoute, sessionId);

        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await replay.Content.ReadAsStringAsync()).Should().Contain("SESSION_IDLE_TIMEOUT");

        // ...and the second observation must not add a second audit row for one timeout.
        await AssertAuditedAsync(sessionId, AuditAction.MfaSessionIdleTimeout);
    }

    [Fact]
    public async Task ShouldKeepReportingExpiry_AfterAbsoluteTimeoutDestroysSession()
    {
        var sessionId = await CompleteMfaSetupAsync();

        await SetExpiresAtAsync(sessionId, DateTime.UtcNow.AddMinutes(-1));

        (await SendAsync(HttpMethod.Get, ProtectedRoute, sessionId)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var replay = await SendAsync(HttpMethod.Get, ProtectedRoute, sessionId);

        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await replay.Content.ReadAsStringAsync()).Should().Contain("SESSION_EXPIRED");
        await AssertAuditedAsync(sessionId, AuditAction.MfaSessionExpired);
    }

    [Fact]
    public async Task ShouldNotDestroyAnotherUsersTimedOutSession()
    {
        // The shared-browser case. mfa_session is a persistent cookie, so the next person to sign in on this
        // machine arrives holding the previous user's cookie. Acting on it would revoke their session, audit
        // it against them, and tell the new arrival they had been timed out seconds after signing in.
        await CompleteMfaSetupAsync();

        await Context.Users.AddAsync(AdminsData.SecondAdmin);
        await SaveChangesAsync();
        var otherUsersSessionId = await SeedDeviceBoundSessionAsync(AdminsData.SecondAdmin.Id);
        await SetLastActivityAsync(otherUsersSessionId, DateTime.UtcNow.AddMinutes(-_settings.Mfa.IdleTimeoutMinutes - 1));

        var response = await SendAsync(HttpMethod.Get, ProtectedRoute, otherUsersSessionId);

        // Told to verify, not told they timed out — and crucially not signed out of CIAM.
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Contain("MFA_REQUIRED");

        (await GetSessionAsync(otherUsersSessionId)).IsRevoked.Should().BeFalse();

        Context.ChangeTracker.Clear();
        var audit = await Context.AuditLogs.AsNoTracking()
            .Where(x => x.TargetType == AuditTargetType.MfaSession && x.TargetId == otherUsersSessionId)
            .ToListAsync();
        audit.Should().NotContain(x => x.Action == AuditAction.MfaSessionIdleTimeout);
    }

    [Fact]
    public async Task ShouldNotSlideAnotherUsersSession()
    {
        // Sliding is a write, so presenting somebody else's cookie must not hold their session open. The
        // seeded session carries this device's real binding hashes, so it is the ownership check — not the
        // device binding — that has to do the refusing here.
        await CompleteMfaSetupAsync();

        await Context.Users.AddAsync(AdminsData.SecondAdmin);
        await SaveChangesAsync();
        var otherUsersSessionId = await SeedDeviceBoundSessionAsync(AdminsData.SecondAdmin.Id);

        var staleActivity = DateTime.UtcNow.AddMinutes(-5);
        await SetLastActivityAsync(otherUsersSessionId, staleActivity);

        var response = await SendAsync(HttpMethod.Get, ProtectedRoute, otherUsersSessionId);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Contain("MFA_REQUIRED");
        (await GetSessionAsync(otherUsersSessionId)).LastActivityAt.Should().BeCloseTo(staleActivity, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task ShouldNotBlockExemptRoutes_WhenSessionHasTimedOut()
    {
        var sessionId = await CompleteMfaSetupAsync();

        await SetLastActivityAsync(sessionId, DateTime.UtcNow.AddMinutes(-_settings.Mfa.IdleTimeoutMinutes - 1));

        // /health is exempt from the gate, so a timed-out cookie must not take the liveness probe down with
        // it — and re-verifying at /mfa/* has to stay reachable for the user to recover.
        var response = await SendAsync(HttpMethod.Get, "health", sessionId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ShouldAllowAndAuditTheRequest_WhenOnlyTheClientIpChanges()
    {
        // BLUM-7848. Production mfa.session.device_mismatch rows showed live sessions refused with the browser
        // matching and only the address moved, sending users back to the code-entry screen mid-form. With the
        // IP half of the binding off (the default) that request goes through, and leaves an ip_changed row
        // carrying the same before/after evidence a refusal would.
        _settings.Mfa.BindSessionToIpAddress = false;
        var sessionId = await CompleteMfaSetupAsync();

        var response = await SendAsync(HttpMethod.Get, ProtectedRoute, sessionId, clientIp: OtherClientIp);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetSessionAsync(sessionId)).IsRevoked.Should().BeFalse();

        Context.ChangeTracker.Clear();
        var rows = await Context.AuditLogs.AsNoTracking()
            .Where(x => x.TargetType == AuditTargetType.MfaSession && x.TargetId == sessionId)
            .ToListAsync();

        rows.Should().NotContain(x => x.Action == AuditAction.MfaSessionDeviceMismatch);
        var row = rows.Should().ContainSingle(x => x.Action == AuditAction.MfaSessionIpChanged).Subject;
        row.ActorUserId.Should().Be(AdminsData.MainAdmin.Id);

        JsonNode.Parse(row.Before!)!["ip"]!.GetValue<string>().Should().Be(ClientIp);

        var after = JsonNode.Parse(row.After!)!;
        after["ip"]!.GetValue<string>().Should().Be(OtherClientIp);
        after["ipMatches"]!.GetValue<bool>().Should().BeFalse();
        after["userAgentMatches"]!.GetValue<bool>().Should().BeTrue();

        (await GetSessionAsync(sessionId)).LastSeenIpAddress.Should().Be(OtherClientIp);
    }

    [Fact]
    public async Task ShouldRecordAnIpChangeOncePerMove_NotOnEveryRequestAfterIt()
    {
        // BLUM-7848 review. Compared against the sign-in address, one office-to-home move re-fired ip_changed
        // on every later request, spending the user's hourly budget on identical rows — so a replay from a
        // third address that hour was the one suppressed.
        _settings.Mfa.BindSessionToIpAddress = false;
        var sessionId = await CompleteMfaSetupAsync();

        for (var i = 0; i < 5; i++)
        {
            (await SendAsync(HttpMethod.Get, ProtectedRoute, sessionId, clientIp: OtherClientIp)).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        (await SendAsync(HttpMethod.Get, ProtectedRoute, sessionId, clientIp: ThirdClientIp)).StatusCode.Should().Be(HttpStatusCode.OK);

        var rows = await GetIpChangeAuditAsync(sessionId);
        rows.Should().HaveCount(2, "two moves, however many requests followed them");

        var third = rows.Single(x => JsonNode.Parse(x.After!)!["ip"]!.GetValue<string>() == ThirdClientIp);
        JsonNode.Parse(third.Before!)!["ip"]!.GetValue<string>().Should().Be(OtherClientIp, "Before is where it moved from, not where it signed in");
    }

    [Fact]
    public async Task ShouldRecordEachAddressOnce_WhenAProxyPoolAlternatesBetweenThem()
    {
        // Two addresses alternating on every request is a transition every time; each one is recorded once.
        _settings.Mfa.BindSessionToIpAddress = false;
        var sessionId = await CompleteMfaSetupAsync();

        foreach (var ip in new[] { OtherClientIp, ClientIp, OtherClientIp, ClientIp, OtherClientIp })
        {
            (await SendAsync(HttpMethod.Get, ProtectedRoute, sessionId, clientIp: ip)).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        (await GetIpChangeAuditAsync(sessionId)).Should().HaveCount(2);
    }

    [Fact]
    public async Task ShouldShowWhereTheSessionWasLastUsed_OnTheSessionsList()
    {
        // With the IP binding off, a replayed cookie that copies the User-Agent is let through; the sessions
        // list is where the user can see it was used from somewhere else.
        _settings.Mfa.BindSessionToIpAddress = false;
        var sessionId = await CompleteMfaSetupAsync();

        var before = await (await SendAsync(HttpMethod.Get, "profile/sessions", sessionId)).ToResponseModel<List<MfaSessionListItemDto>>();
        before.Single(x => x.Id == sessionId).LastSeenIpAddress.Should().BeNull();

        // Read from the new address too: a request from the sign-in address would itself be a move back.
        var after = await (await SendAsync(HttpMethod.Get, "profile/sessions", sessionId, clientIp: OtherClientIp)).ToResponseModel<List<MfaSessionListItemDto>>();
        var row = after.Single(x => x.Id == sessionId);
        row.IpAddress.Should().Be(ClientIp);
        row.LastSeenIpAddress.Should().Be(OtherClientIp);
    }

    [Fact]
    public async Task ShouldNotAuditAnIpChange_WhenTheAddressIsUnchanged()
    {
        _settings.Mfa.BindSessionToIpAddress = false;
        var sessionId = await CompleteMfaSetupAsync();

        (await SendAsync(HttpMethod.Get, ProtectedRoute, sessionId)).StatusCode.Should().Be(HttpStatusCode.OK);

        Context.ChangeTracker.Clear();
        var rows = await Context.AuditLogs.AsNoTracking()
            .Where(x => x.TargetId == sessionId && x.Action == AuditAction.MfaSessionIpChanged)
            .CountAsync();

        rows.Should().Be(0);
    }

    [Fact]
    public async Task ShouldNotCopyTheSignInAddressIntoTheAuditLog_WhenASessionIsCreated()
    {
        // BLUM-7848 review. audit_logs has no retention sweep, so copying the address and device into the
        // created row would keep them indefinitely, past mfa_sessions' SessionRetentionDays. They go to the log
        // and telemetry instead; the row's target id leads to the session for as long as retention allows.
        var sessionId = await CompleteMfaSetupAsync();

        Context.ChangeTracker.Clear();
        var row = await Context.AuditLogs.AsNoTracking()
            .SingleAsync(x => x.TargetId == sessionId && x.Action == AuditAction.MfaSessionCreated);

        row.Before.Should().BeNull();
        row.After.Should().BeNull();
    }

    [Fact]
    public async Task ShouldStillRefuseAndAuditTheBinding_WhenTheClientIpChangesAndIpBindingIsOn()
    {
        // BLUM-8321, under the strict binding BLUM-7848 made opt-in. The refusal must leave a record saying
        // what the session was bound to and what the request presented.
        _settings.Mfa.BindSessionToIpAddress = true;
        var sessionId = await CompleteMfaSetupAsync();

        var response = await SendAsync(HttpMethod.Get, ProtectedRoute, sessionId, clientIp: OtherClientIp);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Contain("MFA_REQUIRED");
        (await GetSessionAsync(sessionId)).IsRevoked.Should().BeFalse();

        var row = await GetSingleDeviceMismatchAuditAsync(sessionId);
        row.ActorUserId.Should().Be(AdminsData.MainAdmin.Id);

        var before = JsonNode.Parse(row.Before!)!;
        before["ip"]!.GetValue<string>().Should().Be(ClientIp);
        before["device"]!.GetValue<string>().Should().Be(DeviceLabelResolver.Resolve(TestUserAgent));

        var after = JsonNode.Parse(row.After!)!;
        after["ip"]!.GetValue<string>().Should().Be(OtherClientIp);
        after["device"]!.GetValue<string>().Should().Be(DeviceLabelResolver.Resolve(TestUserAgent));
        after["ipMatches"]!.GetValue<bool>().Should().BeFalse();
        after["userAgentMatches"]!.GetValue<bool>().Should().BeTrue();
        after["sessionAgeSeconds"]!.GetValue<long>().Should().BeGreaterThanOrEqualTo(0);
        after["idleSeconds"]!.GetValue<long>().Should().BeGreaterThanOrEqualTo(0);
        after["suppressedSincePrevious"]!.GetValue<int>().Should().Be(0);
    }

    [Fact]
    public async Task ShouldRecordWhichHalfMoved_WhenOnlyTheUserAgentChanges()
    {
        // Refused whatever BindSessionToIpAddress says — the browser half of the binding is never relaxed.
        _settings.Mfa.BindSessionToIpAddress = false;
        var sessionId = await CompleteMfaSetupAsync();

        var response = await SendAsync(HttpMethod.Get, ProtectedRoute, sessionId, userAgent: OtherUserAgent);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Contain("MFA_REQUIRED");

        var row = await GetSingleDeviceMismatchAuditAsync(sessionId);

        JsonNode.Parse(row.Before!)!["ip"]!.GetValue<string>().Should().Be(ClientIp);

        var after = JsonNode.Parse(row.After!)!;
        after["ip"]!.GetValue<string>().Should().Be(ClientIp);
        after["device"]!.GetValue<string>().Should().Be(DeviceLabelResolver.Resolve(OtherUserAgent));
        after["ipMatches"]!.GetValue<bool>().Should().BeTrue();
        after["userAgentMatches"]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public async Task ShouldCapDeviceMismatchAuditRowsPerUser()
    {
        // The address comes from a header the caller sets, so without a cap a signed-in user could make the
        // service commit an audit row per request just by varying it. Its own user: the throttle is a
        // singleton that outlives each test method, and this one spends the whole hourly budget.
        await Context.Users.AddAsync(AdminsData.SecondAdmin);
        await SaveChangesAsync();
        TestIdentityContext.Set(new TestIdentity(Role.Admin, AdminsData.SecondAdmin.Id.Value, null, Email: AdminsData.SecondAdmin.Email));

        var cap = _settings.Mfa.MaxDeviceMismatchAuditRowsPerUserPerHour;
        cap.Should().BePositive("a cap of 0 means unlimited, and this test would then prove nothing");

        var sessionId = await CompleteMfaSetupAsync();

        for (var attempt = 0; attempt <= cap; attempt++)
        {
            var response = await SendAsync(HttpMethod.Get, ProtectedRoute, sessionId, userAgent: OtherUserAgent);
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        Context.ChangeTracker.Clear();
        var rows = await Context.AuditLogs.AsNoTracking()
            .Where(x => x.Action == AuditAction.MfaSessionDeviceMismatch && x.TargetId == sessionId)
            .CountAsync();

        rows.Should().Be(cap);
    }

    private async Task<List<AuditLog>> GetIpChangeAuditAsync(Guid sessionId)
    {
        Context.ChangeTracker.Clear();

        return await Context.AuditLogs
            .AsNoTracking()
            .Where(x => x.TargetType == AuditTargetType.MfaSession && x.TargetId == sessionId)
            .Where(x => x.Action == AuditAction.MfaSessionIpChanged)
            .ToListAsync();
    }

    private async Task<AuditLog> GetSingleDeviceMismatchAuditAsync(Guid sessionId)
    {
        Context.ChangeTracker.Clear();

        var audit = await Context.AuditLogs
            .AsNoTracking()
            .Where(x => x.TargetType == AuditTargetType.MfaSession && x.TargetId == sessionId)
            .Where(x => x.Action == AuditAction.MfaSessionDeviceMismatch)
            .ToListAsync();

        return audit.Should().ContainSingle().Subject;
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string route,
        Guid? sessionId,
        string clientIp = ClientIp,
        string? userAgent = null)
    {
        var request = new HttpRequestMessage(method, route);
        request.Headers.Add("X-Azure-ClientIP", clientIp);

        // A per-request User-Agent replaces the one BaseIntegrationTest fixes on the shared client: HttpClient
        // only adds a default header the request does not already carry.
        if (userAgent is not null)
        {
            request.Headers.UserAgent.ParseAdd(userAgent);
        }

        if (sessionId is not null)
        {
            // The cookie is Secure and the test client's base address is http://localhost, so the handler's
            // CookieContainer will never echo it back — set the header explicitly instead.
            request.Headers.Add("Cookie", $"{MfaSessionCookieName}={sessionId}");
        }

        return await Client.SendAsync(request);
    }

    private async Task<Guid> CompleteMfaSetupAsync()
    {
        var setupResponse = await SendAsync(HttpMethod.Post, "mfa/setup", sessionId: null);
        var setup = await setupResponse.ToResponseModel<MfaSetupResponseDto>();

        var verifyRequest = new HttpRequestMessage(HttpMethod.Post, "mfa/setup/verify")
        {
            Content = JsonContent.Create(new MfaVerifyRequestDto(GenerateTotpCode(setup.ManualEntryKey))),
        };
        verifyRequest.Headers.Add("X-Azure-ClientIP", ClientIp);

        var verifyResponse = await Client.SendAsync(verifyRequest);
        verifyResponse.IsSuccessStatusCode.Should().BeTrue();

        var complete = await verifyResponse.ToResponseModel<MfaSetupCompleteResponseDto>();
        complete.SessionId.Should().NotBe(Guid.Empty);

        return complete.SessionId;
    }

    // A session bound to the same device this class's requests come from, so validation reaches the ownership
    // check instead of stopping at the device binding. The hashes must match MfaSessionService.ComputeHash —
    // base64 of the SHA-256, not hex.
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

        await Context.MfaSessions.AddAsync(session);
        await SaveChangesAsync();

        return session.Id.Value;
    }

    private static string HashForBinding(string input) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(input)));

    // Back-dating through the database rather than the domain: MfaSession deliberately exposes no setter for
    // either timestamp, and a test-only one would be a hole in the aggregate purely to make assertions easy.
    private async Task SetLastActivityAsync(Guid sessionId, DateTime lastActivityAt)
    {
        await Context.MfaSessions
            .Where(x => x.Id == new MfaSessionId(sessionId))
            .ExecuteUpdateAsync(x => x.SetProperty(s => s.LastActivityAt, lastActivityAt));

        Context.ChangeTracker.Clear();
    }

    private async Task SetExpiresAtAsync(Guid sessionId, DateTime expiresAt)
    {
        await Context.MfaSessions
            .Where(x => x.Id == new MfaSessionId(sessionId))
            .ExecuteUpdateAsync(x => x.SetProperty(s => s.ExpiresAt, expiresAt));

        Context.ChangeTracker.Clear();
    }

    private async Task<MfaSession> GetSessionAsync(Guid sessionId)
    {
        // The base Context lives in its own long-lived scope, so it still tracks the entity as it was before
        // the request under test touched it.
        Context.ChangeTracker.Clear();

        return await Context.MfaSessions
            .AsNoTracking()
            .FirstAsync(x => x.Id == new MfaSessionId(sessionId));
    }

    private async Task AssertAuditedAsync(Guid sessionId, string action)
    {
        Context.ChangeTracker.Clear();

        var audit = await Context.AuditLogs
            .AsNoTracking()
            .Where(x => x.TargetType == AuditTargetType.MfaSession && x.TargetId == sessionId)
            .ToListAsync();

        audit.Should().ContainSingle(x => x.Action == action);
    }

    private static string GenerateTotpCode(string base32Secret)
    {
        var secretBytes = Base32Encoding.ToBytes(base32Secret);
        var totp = new Totp(secretBytes, step: 30, totpSize: 6);
        return totp.ComputeTotp();
    }

    public async Task InitializeAsync()
    {
        _settings.Mfa.BypassForTesting = false;
        _settings.Mfa.BindSessionToIpAddress = false;

        Context.MfaSessions.RemoveRange(Context.MfaSessions);
        Context.UserMfas.RemoveRange(Context.UserMfas);
        Context.AuditLogs.RemoveRange(Context.AuditLogs);
        Context.OrganisationUsers.RemoveRange(Context.OrganisationUsers);
        Context.Users.RemoveRange(Context.Users);
        Context.Organisations.RemoveRange(Context.Organisations);
        await SaveChangesAsync();

        await Context.Users.AddAsync(AdminsData.MainAdmin);
        await SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        _settings.Mfa.BypassForTesting = true;
        _settings.Mfa.BindSessionToIpAddress = false;

        Context.MfaSessions.RemoveRange(Context.MfaSessions);
        Context.UserMfas.RemoveRange(Context.UserMfas);
        Context.AuditLogs.RemoveRange(Context.AuditLogs);
        Context.OrganisationUsers.RemoveRange(Context.OrganisationUsers);
        Context.Users.RemoveRange(Context.Users);
        Context.Organisations.RemoveRange(Context.Organisations);
        await SaveChangesAsync();

        AuthenticateAsAdmin();
    }
}