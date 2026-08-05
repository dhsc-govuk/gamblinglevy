using System.Net;
using System.Net.Http.Json;
using Application.Common.Settings;
using Application.Mfa.Dtos;
using Domain.Organisations;
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
/// BLUM-7242 / CWE-613 — logout must destroy the server-side session record, not just the cookie.
///
/// Unlike every other test class, this one runs with the MFA gate switched ON: the whole point is to
/// exercise MfaRequiredMiddleware, which IntegrationTestWebFactory otherwise short-circuits via
/// ApplicationSettings.Mfa.BypassForTesting. BaseIntegrationTest is IClassFixture-scoped, so the settings
/// singleton belongs to this class alone and flipping it cannot leak into a parallel class.
/// </summary>
public class MfaSessionRevocationTests : BaseIntegrationTest, IAsyncLifetime
{
    // Any endpoint outside the middleware's "/mfa/", "/swagger", "/health" exemptions will do; this one is
    // admin-only, takes no route parameters, and the default test identity is a platform admin.
    private const string ProtectedRoute = "admin/users";
    private const string MfaSessionCookieName = "mfa_session";

    // MfaSession binds to a SHA-256 of the client IP and User-Agent and compares them for exact equality,
    // so every request in this class has to present the same pair. The base class already fixes the
    // User-Agent; X-Azure-ClientIP is the first source MfaRequiredMiddleware checks.
    private const string ClientIp = "203.0.113.10";

    private readonly ApplicationSettings _settings;

    public MfaSessionRevocationTests(IntegrationTestWebFactory factory)
        : base(factory)
    {
        _settings = factory.Services.GetRequiredService<ApplicationSettings>();
    }

    [Fact]
    public async Task ShouldRevokeSessionServerSide_OnLogout()
    {
        var sessionId = await CompleteMfaSetupAsync();

        var response = await SendAsync(HttpMethod.Post, "mfa/logout", sessionId);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var session = await GetSessionAsync(sessionId);
        session.IsRevoked.Should().BeTrue();
    }

    [Fact]
    public async Task ShouldClearSessionCookie_OnLogout()
    {
        var sessionId = await CompleteMfaSetupAsync();

        var response = await SendAsync(HttpMethod.Post, "mfa/logout", sessionId);

        response.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeTrue();
        cookies.Should().Contain(c => c.StartsWith($"{MfaSessionCookieName}=;", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ShouldRejectReplayedSessionCookie_AfterLogout()
    {
        var sessionId = await CompleteMfaSetupAsync();

        // Sanity check: the very same cookie works before logout, so a failure below is the revocation
        // taking effect rather than the middleware refusing everything.
        var beforeLogout = await SendAsync(HttpMethod.Get, ProtectedRoute, sessionId);
        beforeLogout.StatusCode.Should().Be(HttpStatusCode.OK);

        await SendAsync(HttpMethod.Post, "mfa/logout", sessionId);

        var replay = await SendAsync(HttpMethod.Get, ProtectedRoute, sessionId);

        // 401 SESSION_REVOKED rather than 403 MFA_REQUIRED since BLUM-7241. A revocation is deliberate — a
        // logout here, but equally a user ending this device from their sessions list — and MFA_REQUIRED
        // would send the browser to the code entry screen, where a correct code mints a fresh session and
        // quietly undoes it. The client answers this one by ending the CIAM sign-in instead.
        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await replay.Content.ReadAsStringAsync()).Should().Contain("SESSION_REVOKED");
    }

    [Fact]
    public async Task ShouldRevokeAllCallerSessions_WhenLogoutCarriesNoCookie()
    {
        var sessionId = await CompleteMfaSetupAsync();
        var secondSessionId = await SeedSessionAsync(AdminsData.MainAdmin.Id);

        var response = await SendAsync(HttpMethod.Post, "mfa/logout", sessionId: null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetSessionAsync(sessionId)).IsRevoked.Should().BeTrue();
        (await GetSessionAsync(secondSessionId)).IsRevoked.Should().BeTrue();
    }

    [Fact]
    public async Task ShouldLeaveAnotherUsersSessionIntact_OnLogout()
    {
        var ownSessionId = await CompleteMfaSetupAsync();

        await Context.Users.AddAsync(AdminsData.SecondAdmin);
        await SaveChangesAsync();
        var otherUsersSessionId = await SeedSessionAsync(AdminsData.SecondAdmin.Id);

        var response = await SendAsync(HttpMethod.Post, "mfa/logout", otherUsersSessionId);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetSessionAsync(otherUsersSessionId)).IsRevoked.Should().BeFalse();

        // The caller presented a session that isn't theirs, so nothing of theirs is revoked either.
        // There is only one mfa_session cookie slot per host, so a caller holding someone else's cookie
        // has no session of their own on this browser to sweep.
        (await GetSessionAsync(ownSessionId)).IsRevoked.Should().BeFalse();
    }

    [Fact]
    public async Task ShouldAllowLogout_WhenOrganisationIsSuspended()
    {
        // OrganisationSuspendedMiddleware runs before the MFA gate and exempts almost nothing. While it
        // blocked /mfa/logout, the SPA interceptor turned the 403 into a promise that never settles, so a
        // suspended user could never finish signing out and their session was never revoked.
        var (userId, organisationId) = await SeedSuspendedOrganisationUserAsync("E06000099");
        var sessionId = await SeedSessionAsync(userId);

        AuthenticateAs(Role.OrganisationAdmin, userId.Value, organisationId.Value);

        var response = await SendAsync(HttpMethod.Post, "mfa/logout", sessionId);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetSessionAsync(sessionId)).IsRevoked.Should().BeTrue();
    }

    [Fact]
    public async Task ShouldStillBlockOtherRoutes_WhenOrganisationIsSuspended()
    {
        var (userId, organisationId) = await SeedSuspendedOrganisationUserAsync("E06000098");

        AuthenticateAs(Role.OrganisationAdmin, userId.Value, organisationId.Value);

        // Exempting logout must not widen into a general suspension bypass.
        var response = await SendAsync(HttpMethod.Get, "activities", sessionId: null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Contain("ORGANISATION_SUSPENDED");
    }

    // Seeds a suspended organisation and a user inside it. The organisation must exist before
    // AuthenticateAs, or TestAuthHandler emits no organisationStatus claim, the suspension middleware
    // never engages, and both tests above pass vacuously.
    private async Task<(UserId UserId, OrganisationId OrganisationId)> SeedSuspendedOrganisationUserAsync(string onsCode)
    {
        var organisation = Organisation.New(
            OrganisationId.New(),
            "Suspended Council",
            onsCode,
            Region.London,
            OrgType.LocalAuthority,
            "E1000001",
            OnboardingStatus.Suspended);

        await Context.Organisations.AddAsync(organisation);
        await SaveChangesAsync();

        var organisationUser = OrganisationUsersData.MainOrganisationUser(organisation.Id);
        await Context.OrganisationUsers.AddAsync(organisationUser);
        await SaveChangesAsync();

        return (organisationUser.UserId, organisation.Id);
    }

    [Fact]
    public async Task ShouldDenyProtectedRoutes_WhenMfaSetupStartedButNeverVerified()
    {
        // POST /mfa/setup persists a user_mfa row with IsEnabled == false. That state used to fall through
        // the middleware, handing anyone who abandoned the setup page unrestricted access with no session.
        var setup = await SendAsync(HttpMethod.Post, "mfa/setup", sessionId: null);
        setup.IsSuccessStatusCode.Should().BeTrue();

        var response = await SendAsync(HttpMethod.Get, ProtectedRoute, sessionId: null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Contain("MFA_SETUP_REQUIRED");
    }

    [Fact]
    public async Task ShouldDenyProtectedRoutes_WhenNoMfaRecordExists()
    {
        var response = await SendAsync(HttpMethod.Get, ProtectedRoute, sessionId: null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Contain("MFA_SETUP_REQUIRED");
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

    private async Task<Guid> SeedSessionAsync(UserId userId)
    {
        var session = MfaSession.New(
            MfaSessionId.New(),
            userId,
            ipAddressHash: "seeded-ip-hash",
            userAgentHash: "seeded-user-agent-hash",
            ipAddress: "203.0.113.10",
            deviceLabel: "Chrome on Windows");

        await Context.MfaSessions.AddAsync(session);
        await SaveChangesAsync();

        return session.Id.Value;
    }

    private async Task<MfaSession> GetSessionAsync(Guid sessionId)
    {
        // The base Context lives in its own long-lived scope, so it still tracks the pre-logout entity.
        Context.ChangeTracker.Clear();

        return await Context.MfaSessions
            .AsNoTracking()
            .FirstAsync(x => x.Id == new MfaSessionId(sessionId));
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