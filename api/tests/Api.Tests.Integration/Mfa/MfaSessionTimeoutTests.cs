using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
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

    // The session binds to a SHA-256 of the client IP and User-Agent and compares them for exact equality,
    // so every request here has to present the same pair. The base class already fixes the User-Agent;
    // X-Azure-ClientIP is the first source MfaRequiredMiddleware checks.
    private const string ClientIp = "203.0.113.10";

    // Mirrors the User-Agent BaseIntegrationTest fixes on the shared HttpClient.
    private const string TestUserAgent = "IntegrationTest/1.0";

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
            deviceLabel: "Chrome on Windows");

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