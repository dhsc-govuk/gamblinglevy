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
using Tests.Common.Services;
using Tests.Data;
using Xunit;

namespace Api.Tests.Integration.Mfa;

/// <summary>
/// BLUM-7241 / CWE-613 — unlimited concurrent sessions with no user notification. A user must be able to see
/// every device signed in to their account, end one of them immediately, and be told out of band when a
/// sign-in happens on a device that was not already signed in.
///
/// Like the other two MFA session classes this runs with the gate switched ON: revocation is only observable
/// through MfaRequiredMiddleware, which IntegrationTestWebFactory otherwise short-circuits via
/// ApplicationSettings.Mfa.BypassForTesting. BaseIntegrationTest is IClassFixture-scoped, so the settings
/// singleton belongs to this class alone and flipping it cannot leak into a class running in parallel.
/// </summary>
public class MfaConcurrentSessionTests : BaseIntegrationTest, IAsyncLifetime
{
    // Outside the middleware's "/mfa/", "/swagger", "/health" exemptions, takes no route parameters, and the
    // default test identity is a platform admin.
    private const string ProtectedRoute = "admin/users";
    private const string SessionsRoute = "profile/sessions";
    private const string MfaSessionCookieName = "mfa_session";

    // Three distinct (IP, User-Agent) pairs — the device fingerprint is the hash of both, so these are three
    // different devices as far as the session binding is concerned. The IPs are unique to this class because
    // InMemoryNewSignInEmailService's store is static and shared with any class running alongside this one.
    private const string LaptopIp = "203.0.113.50";
    private const string LaptopUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";

    private const string PhoneIp = "198.51.100.42";
    private const string PhoneUserAgent = "Mozilla/5.0 (iPhone; CPU iPhone OS 17_4 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.4 Mobile/15E148 Safari/604.1";

    private const string TabletIp = "192.0.2.77";
    private const string TabletUserAgent = "Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Mobile Safari/537.36";

    private static readonly string[] ClassIpAddresses = [LaptopIp, PhoneIp, TabletIp];

    private readonly ApplicationSettings _settings;

    // Captured from enrolment so later sign-ins can mint fresh codes for the same authenticator.
    private string _authenticatorSecret = string.Empty;

    public MfaConcurrentSessionTests(IntegrationTestWebFactory factory)
        : base(factory)
    {
        _settings = factory.Services.GetRequiredService<ApplicationSettings>();
    }

    [Fact]
    public async Task ShouldListActiveSessions_WithDeviceMetadata_AndFlagTheCallersOwn()
    {
        var laptopSession = await EnrolAsync(LaptopIp, LaptopUserAgent);
        var phoneSession = await SignInAsync(PhoneIp, PhoneUserAgent);

        var response = await SendAsync(HttpMethod.Get, SessionsRoute, LaptopIp, LaptopUserAgent, laptopSession);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var sessions = await response.ToResponseModel<List<MfaSessionListItemDto>>();

        sessions.Should().HaveCount(2);

        // AC3: device, IP and timestamps, resolved server-side — the browser is never asked what it is.
        var laptop = sessions.Single(x => x.Id == laptopSession);
        laptop.IsCurrent.Should().BeTrue();
        laptop.DeviceLabel.Should().Be("Chrome on Windows");
        laptop.IpAddress.Should().Be(LaptopIp);
        laptop.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(5));
        laptop.ExpiresAt.Should().BeAfter(DateTime.UtcNow);

        // Marking only the caller's own row is what lets the user end the others without signing themselves out.
        var phone = sessions.Single(x => x.Id == phoneSession);
        phone.IsCurrent.Should().BeFalse();
        phone.DeviceLabel.Should().Be("Safari on iPhone");
        phone.IpAddress.Should().Be(PhoneIp);
    }

    [Fact]
    public async Task ShouldNotListAnotherUsersSessions()
    {
        var laptopSession = await EnrolAsync(LaptopIp, LaptopUserAgent);
        var otherUsersSession = await SeedSessionForOtherUserAsync();

        var response = await SendAsync(HttpMethod.Get, SessionsRoute, LaptopIp, LaptopUserAgent, laptopSession);

        var sessions = await response.ToResponseModel<List<MfaSessionListItemDto>>();

        sessions.Should().ContainSingle(x => x.Id == laptopSession);
        sessions.Should().NotContain(x => x.Id == otherUsersSession);
    }

    [Fact]
    public async Task ShouldNotListRevokedOrTimedOutSessions()
    {
        var laptopSession = await EnrolAsync(LaptopIp, LaptopUserAgent);
        var phoneSession = await SignInAsync(PhoneIp, PhoneUserAgent);
        var tabletSession = await SignInAsync(TabletIp, TabletUserAgent);

        await RevokeDirectlyAsync(phoneSession);

        // Timed-out sessions are only destroyed lazily, by the next request that presents them, so a list
        // filtered on IsRevoked alone would still offer this one — inviting the user to revoke something that
        // could not carry a request anyway.
        await SetLastActivityAsync(tabletSession, DateTime.UtcNow.AddMinutes(-_settings.Mfa.IdleTimeoutMinutes - 1));

        var response = await SendAsync(HttpMethod.Get, SessionsRoute, LaptopIp, LaptopUserAgent, laptopSession);

        var sessions = await response.ToResponseModel<List<MfaSessionListItemDto>>();

        sessions.Should().ContainSingle();
        sessions.Single().Id.Should().Be(laptopSession);
    }

    [Fact]
    public async Task ShouldRevokeSession_AndSignOutThatDeviceImmediately()
    {
        var laptopSession = await EnrolAsync(LaptopIp, LaptopUserAgent);
        var phoneSession = await SignInAsync(PhoneIp, PhoneUserAgent);

        var revoke = await SendAsync(HttpMethod.Post, $"{SessionsRoute}/{phoneSession}/revoke", LaptopIp, LaptopUserAgent, laptopSession);

        revoke.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetSessionAsync(phoneSession)).IsRevoked.Should().BeTrue();
        await AssertAuditedAsync(phoneSession, AuditAction.MfaSessionRevoked);

        // AC4: immediate, and on the phone's very next request — nothing has to expire first.
        var phoneRequest = await SendAsync(HttpMethod.Get, ProtectedRoute, PhoneIp, PhoneUserAgent, phoneSession);

        // 401 rather than 403 MFA_REQUIRED on purpose: MFA_REQUIRED sends the SPA to the code entry screen,
        // where a correct code would mint a new session and quietly undo the revocation.
        phoneRequest.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await phoneRequest.Content.ReadAsStringAsync()).Should().Contain("SESSION_REVOKED");

        // ...and the device that did the revoking carries on unaffected.
        var laptopRequest = await SendAsync(HttpMethod.Get, ProtectedRoute, LaptopIp, LaptopUserAgent, laptopSession);
        laptopRequest.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ShouldReturnNotFound_WhenRevokingAnotherUsersSession()
    {
        var laptopSession = await EnrolAsync(LaptopIp, LaptopUserAgent);
        var otherUsersSession = await SeedSessionForOtherUserAsync();

        var response = await SendAsync(HttpMethod.Post, $"{SessionsRoute}/{otherUsersSession}/revoke", LaptopIp, LaptopUserAgent, laptopSession);

        // Answered identically to an id that does not exist, so this cannot be used to discover whose
        // sessions are real.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await GetSessionAsync(otherUsersSession)).IsRevoked.Should().BeFalse();
    }

    [Fact]
    public async Task ShouldReturnNotFound_WhenRevokingAnUnknownSession()
    {
        var laptopSession = await EnrolAsync(LaptopIp, LaptopUserAgent);

        var response = await SendAsync(HttpMethod.Post, $"{SessionsRoute}/{Guid.NewGuid()}/revoke", LaptopIp, LaptopUserAgent, laptopSession);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ShouldEvictLeastRecentlyActiveSession_WhenConcurrentLimitExceeded()
    {
        // AC2, the "previous session invalidated" arm: with a policy configured, a sign-in past the cap ends
        // the oldest rather than letting sessions accumulate without limit.
        _settings.Mfa.MaxConcurrentSessions = 2;

        var laptopSession = await EnrolAsync(LaptopIp, LaptopUserAgent);
        var phoneSession = await SignInAsync(PhoneIp, PhoneUserAgent);

        // Both were created within the same instant, so make the laptop unambiguously the least recently
        // active — but well inside the idle window, or it would not count as a live session at all.
        await SetLastActivityAsync(laptopSession, DateTime.UtcNow.AddMinutes(-5));

        var tabletSession = await SignInAsync(TabletIp, TabletUserAgent);

        (await GetSessionAsync(laptopSession)).IsRevoked.Should().BeTrue();
        await AssertAuditedAsync(laptopSession, AuditAction.MfaSessionEvicted);

        (await GetSessionAsync(phoneSession)).IsRevoked.Should().BeFalse();
        (await GetSessionAsync(tabletSession)).IsRevoked.Should().BeFalse();

        // The evicted device finds out the same way any revoked one does.
        var laptopRequest = await SendAsync(HttpMethod.Get, ProtectedRoute, LaptopIp, LaptopUserAgent, laptopSession);
        laptopRequest.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await laptopRequest.Content.ReadAsStringAsync()).Should().Contain("SESSION_REVOKED");
    }

    [Fact]
    public async Task ShouldNotEvictAnySession_WhenLimitIsDisabled()
    {
        _settings.Mfa.MaxConcurrentSessions = 0;

        var laptopSession = await EnrolAsync(LaptopIp, LaptopUserAgent);
        var phoneSession = await SignInAsync(PhoneIp, PhoneUserAgent);
        var tabletSession = await SignInAsync(TabletIp, TabletUserAgent);

        (await GetSessionAsync(laptopSession)).IsRevoked.Should().BeFalse();
        (await GetSessionAsync(phoneSession)).IsRevoked.Should().BeFalse();
        (await GetSessionAsync(tabletSession)).IsRevoked.Should().BeFalse();
    }

    [Fact]
    public async Task ShouldEmailUser_WhenASessionStartsWhileAnotherIsLive()
    {
        await EnrolAsync(LaptopIp, LaptopUserAgent);

        // AC1's premise: nothing to warn about when this is the only session. An email here would train the
        // user to ignore the one that matters.
        SentToThisClass().Should().BeEmpty();

        var phoneSession = await SignInAsync(PhoneIp, PhoneUserAgent);

        var sent = SentToThisClass();
        sent.Should().ContainSingle();
        sent.Single().Email.Should().Be(AdminsData.MainAdmin.Email);
        sent.Single().Notification.IpAddress.Should().Be(PhoneIp);
        sent.Single().Notification.DeviceLabel.Should().Be("Safari on iPhone");

        // AC5: the notification is an auditable event, written only once SendGrid has accepted the message.
        await AssertAuditedAsync(phoneSession, AuditAction.MfaSessionNotified);
    }

    [Fact]
    public async Task ShouldEmailUser_EvenWhenTheNewSessionLooksIdenticalToOneAlreadySignedIn()
    {
        // The alert must not be suppressible by anything the caller controls. This used to skip the email
        // when an existing session carried the same User-Agent — which an attacker holding stolen credentials
        // can copy in one line, switching off the only out-of-band signal this remediation produces. Same IP
        // and same User-Agent as the live session, and it still sends.
        await EnrolAsync(LaptopIp, LaptopUserAgent);
        SentToThisClass().Should().BeEmpty();

        await SignInAsync(LaptopIp, LaptopUserAgent);

        SentToThisClass().Should().ContainSingle();
    }

    [Fact]
    public async Task ShouldNotEmailUser_WhenNoOtherSessionIsLive()
    {
        // The one condition that does suppress it, and the only one an attacker cannot influence: there was
        // nobody else signed in to warn about.
        await EnrolAsync(LaptopIp, LaptopUserAgent);

        SentToThisClass().Should().BeEmpty();
    }

    [Fact]
    public async Task ShouldClearTheCookie_SoARevokedBrowserCanRecover()
    {
        // Without this the revoked browser is stranded: the client answers 401 by ending the CIAM sign-in
        // rather than re-verifying, so nothing ever overwrites the cookie the way /mfa/verify does. It would
        // re-authenticate, present the same dead cookie, be told the same thing, and be signed straight back
        // out — a loop it cannot break, because the cookie is HttpOnly and the SPA cannot reach it either.
        var laptopSession = await EnrolAsync(LaptopIp, LaptopUserAgent);
        var phoneSession = await SignInAsync(PhoneIp, PhoneUserAgent);

        await SendAsync(HttpMethod.Post, $"{SessionsRoute}/{phoneSession}/revoke", LaptopIp, LaptopUserAgent, laptopSession);

        var phoneRequest = await SendAsync(HttpMethod.Get, ProtectedRoute, PhoneIp, PhoneUserAgent, phoneSession);

        phoneRequest.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        phoneRequest.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeTrue();
        cookies!.Should().Contain(x => x.StartsWith($"{MfaSessionCookieName}=", StringComparison.Ordinal) && x.Contains("expires=", StringComparison.OrdinalIgnoreCase));

        // Having dropped the cookie, the browser's next request carries no session at all — the ordinary
        // MFA_REQUIRED case, which is what routes the user back to /mfa/verify to sign in again.
        var afterCookieCleared = await SendAsync(HttpMethod.Get, ProtectedRoute, PhoneIp, PhoneUserAgent, sessionId: null);
        afterCookieCleared.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await afterCookieCleared.Content.ReadAsStringAsync()).Should().Contain("MFA_REQUIRED");
    }

    [Fact]
    public async Task ShouldClearTheCookie_WhenTheSessionTimedOut()
    {
        var laptopSession = await EnrolAsync(LaptopIp, LaptopUserAgent);

        await SetLastActivityAsync(laptopSession, DateTime.UtcNow.AddMinutes(-_settings.Mfa.IdleTimeoutMinutes - 1));

        var response = await SendAsync(HttpMethod.Get, ProtectedRoute, LaptopIp, LaptopUserAgent, laptopSession);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeTrue();
        cookies!.Should().Contain(x => x.StartsWith($"{MfaSessionCookieName}=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ShouldStillSignIn_WhenTheClientIpHeaderIsTooLongForTheColumn()
    {
        // ip_address is varchar(45) and is fed straight from a header anyone can set, so a 60-character value
        // used to throw Postgres 22001 out of the session INSERT and fail MFA verification outright.
        var overLongIp = new string('a', 60);

        var setupResponse = await SendAsync(HttpMethod.Post, "mfa/setup", overLongIp, LaptopUserAgent, sessionId: null);
        var setup = await setupResponse.ToResponseModel<MfaSetupResponseDto>();
        _authenticatorSecret = setup.ManualEntryKey;

        var verifyResponse = await SendAsync(
            HttpMethod.Post,
            "mfa/setup/verify",
            overLongIp,
            LaptopUserAgent,
            sessionId: null,
            new MfaVerifyRequestDto(GenerateTotpCode(_authenticatorSecret)));

        verifyResponse.IsSuccessStatusCode.Should().BeTrue();

        var complete = await verifyResponse.ToResponseModel<MfaSetupCompleteResponseDto>();
        var stored = await GetSessionAsync(complete.SessionId);

        // Recorded as unknown rather than truncated into something that looks like a real address.
        stored.IpAddress.Should().Be("unknown");
    }

    [Fact]
    public async Task ShouldCountTheNewSessionAgainstTheLimit()
    {
        // The cap used to be judged against a snapshot read *before* the insert, so two simultaneous sign-ins
        // each counted a set missing the other's row and the user ended up over the limit. Counting after the
        // insert is what makes "how many now?" the question being answered.
        _settings.Mfa.MaxConcurrentSessions = 1;

        var laptopSession = await EnrolAsync(LaptopIp, LaptopUserAgent);
        var phoneSession = await SignInAsync(PhoneIp, PhoneUserAgent);

        Context.ChangeTracker.Clear();
        var live = await Context.MfaSessions.AsNoTracking().Where(x => !x.IsRevoked).ToListAsync();

        live.Should().ContainSingle();
        live.Single().Id.Value.Should().Be(phoneSession);

        // The session that just proved possession of the authenticator is never the one evicted.
        (await GetSessionAsync(laptopSession)).IsRevoked.Should().BeTrue();
        (await GetSessionAsync(phoneSession)).IsRevoked.Should().BeFalse();
    }

    [Fact]
    public async Task ShouldEvictTheSurplus_EvenWhenAnotherSessionWasActiveMoreRecently()
    {
        // The eviction used to assume the new session sorted first because its LastActivityAt is "now". The
        // idle-window slide can move an existing session's forward in the milliseconds after the insert, and
        // when it did, Skip(limit) picked the NEW session as surplus, the self-exclusion dropped it, and
        // nothing was evicted at all — the user silently stayed over the cap.
        _settings.Mfa.MaxConcurrentSessions = 1;

        var laptopSession = await EnrolAsync(LaptopIp, LaptopUserAgent);
        var phoneSession = await SignInAsync(PhoneIp, PhoneUserAgent);

        // The losing session now looks the most recently active of the two.
        await SetLastActivityAsync(laptopSession, DateTime.UtcNow.AddMinutes(1));

        var tabletSession = await SignInAsync(TabletIp, TabletUserAgent);

        // Whoever just proved possession of the authenticator keeps their session; everything else goes.
        (await GetSessionAsync(tabletSession)).IsRevoked.Should().BeFalse();
        (await GetSessionAsync(laptopSession)).IsRevoked.Should().BeTrue();
        (await GetSessionAsync(phoneSession)).IsRevoked.Should().BeTrue();

        Context.ChangeTracker.Clear();
        (await Context.MfaSessions.AsNoTracking().CountAsync(x => !x.IsRevoked)).Should().Be(1);
    }

    [Fact]
    public async Task ShouldStillCompleteSignIn_WhenTheSurplusSessionIsRevokedConcurrently()
    {
        // Stands in for any failure during eviction. The session row and its audit entry are committed before
        // this step runs, so throwing here would hand the user a failed verification for a session that
        // exists — and each retry would strand another live-but-unreachable row that still counts towards the
        // cap, evicting their real devices while never signing them in.
        _settings.Mfa.MaxConcurrentSessions = 1;

        var laptopSession = await EnrolAsync(LaptopIp, LaptopUserAgent);

        // Another request got there first: the row the cap is about to evict is already revoked.
        await RevokeDirectlyAsync(laptopSession);

        var phoneSession = await SignInAsync(PhoneIp, PhoneUserAgent);

        phoneSession.Should().NotBe(Guid.Empty);
        (await GetSessionAsync(phoneSession)).IsRevoked.Should().BeFalse();

        // And the already-revoked row is not audited a second time — one eviction is worth one log line.
        Context.ChangeTracker.Clear();
        var audit = await Context.AuditLogs.AsNoTracking()
            .Where(x => x.TargetType == AuditTargetType.MfaSession && x.TargetId == laptopSession)
            .ToListAsync();
        audit.Should().NotContain(x => x.Action == AuditAction.MfaSessionEvicted);
    }

    [Fact]
    public async Task ShouldEmailUser_WhenTheSameBrowserReconnectsFromADifferentIp()
    {
        // A laptop moving from office wifi to 4G re-verifies and is emailed about a sign-in the user just
        // performed. That is the accepted cost of having no suppression rule: the message names the device,
        // the IP and the time, and says no action is needed if it was them. The alternative — trusting the
        // request to tell us it is a known device — is what let a spoofed User-Agent silence the alert.
        await EnrolAsync(LaptopIp, LaptopUserAgent);

        await SignInAsync(PhoneIp, LaptopUserAgent);

        SentToThisClass().Should().ContainSingle();
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string route,
        string ipAddress,
        string userAgent,
        Guid? sessionId,
        object? body = null)
    {
        var request = new HttpRequestMessage(method, route);

        // X-Azure-ClientIP is the first source both MfaController and MfaRequiredMiddleware check. The
        // User-Agent is set per request rather than on the shared client because the whole point here is
        // several different devices; HttpClient only applies its default when the request has none.
        request.Headers.Add("X-Azure-ClientIP", ipAddress);
        request.Headers.TryAddWithoutValidation("User-Agent", userAgent);

        if (sessionId is not null)
        {
            // The cookie is Secure and the test client's base address is http://localhost, so the handler's
            // CookieContainer will never echo it back — set the header explicitly instead.
            request.Headers.Add("Cookie", $"{MfaSessionCookieName}={sessionId}");
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await Client.SendAsync(request);
    }

    private async Task<Guid> EnrolAsync(string ipAddress, string userAgent)
    {
        var setupResponse = await SendAsync(HttpMethod.Post, "mfa/setup", ipAddress, userAgent, sessionId: null);
        var setup = await setupResponse.ToResponseModel<MfaSetupResponseDto>();
        _authenticatorSecret = setup.ManualEntryKey;

        var verifyResponse = await SendAsync(
            HttpMethod.Post,
            "mfa/setup/verify",
            ipAddress,
            userAgent,
            sessionId: null,
            new MfaVerifyRequestDto(GenerateTotpCode(_authenticatorSecret)));

        verifyResponse.IsSuccessStatusCode.Should().BeTrue();

        var complete = await verifyResponse.ToResponseModel<MfaSetupCompleteResponseDto>();
        complete.SessionId.Should().NotBe(Guid.Empty);

        return complete.SessionId;
    }

    // A second (third, …) sign-in for the same already-enrolled user. Re-using the same TOTP code inside its
    // 30-second step is fine: the rate limiter only counts failures, and a successful verify resets it.
    private async Task<Guid> SignInAsync(string ipAddress, string userAgent)
    {
        var response = await SendAsync(
            HttpMethod.Post,
            "mfa/verify",
            ipAddress,
            userAgent,
            sessionId: null,
            new MfaVerifyRequestDto(GenerateTotpCode(_authenticatorSecret)));

        response.IsSuccessStatusCode.Should().BeTrue();

        var dto = await response.ToResponseModel<MfaVerifyResponseDto>();
        return dto.SessionId;
    }

    private async Task<Guid> SeedSessionForOtherUserAsync()
    {
        await Context.Users.AddAsync(AdminsData.SecondAdmin);
        await SaveChangesAsync();

        return await SeedSessionAsync(AdminsData.SecondAdmin.Id);
    }

    // Seeds the session AND the audit entry the real CreateSessionAsync would have written, so a test can
    // tell "the record was deleted" apart from "the history went with it".
    private async Task<Guid> SeedSessionAsync(UserId userId)
    {
        var session = MfaSession.New(
            MfaSessionId.New(),
            userId,
            ipAddressHash: HashForBinding(LaptopIp),
            userAgentHash: HashForBinding(LaptopUserAgent),
            ipAddress: LaptopIp,
            deviceLabel: "Chrome on Windows");

        await Context.MfaSessions.AddAsync(session);
        await Context.AuditLogs.AddAsync(
            AuditLog.Create(userId, AuditTargetType.MfaSession, session.Id.Value, AuditAction.MfaSessionCreated, before: null, after: null));
        await SaveChangesAsync();

        return session.Id.Value;
    }

    private static string HashForBinding(string input) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(input)));

    // Back-dating and revoking through the database rather than the domain: MfaSession deliberately exposes
    // no setter for its timestamps, and a test-only one would be a hole in the aggregate.
    private async Task SetLastActivityAsync(Guid sessionId, DateTime lastActivityAt)
    {
        await Context.MfaSessions
            .Where(x => x.Id == new MfaSessionId(sessionId))
            .ExecuteUpdateAsync(x => x.SetProperty(s => s.LastActivityAt, lastActivityAt));

        Context.ChangeTracker.Clear();
    }

    private async Task RevokeDirectlyAsync(Guid sessionId)
    {
        await Context.MfaSessions
            .Where(x => x.Id == new MfaSessionId(sessionId))
            .ExecuteUpdateAsync(x => x.SetProperty(s => s.IsRevoked, true));

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

    // InMemoryNewSignInEmailService keeps a static store, and test classes run in parallel, so filter to the
    // devices only this class uses rather than asserting on the whole bag.
    private static IReadOnlyList<(string Email, Application.Common.Interfaces.NewSignInNotification Notification)> SentToThisClass() =>
        InMemoryNewSignInEmailService.Sent
            .Where(x => ClassIpAddresses.Contains(x.Notification.IpAddress))
            .ToList();

    private static string GenerateTotpCode(string base32Secret)
    {
        var secretBytes = Base32Encoding.ToBytes(base32Secret);
        var totp = new Totp(secretBytes, step: 30, totpSize: 6);
        return totp.ComputeTotp();
    }

    public async Task InitializeAsync()
    {
        _settings.Mfa.BypassForTesting = false;
        _settings.Mfa.MaxConcurrentSessions = 5;
        _settings.Mfa.SessionRetentionDays = 30;

        InMemoryNewSignInEmailService.Clear();

        await ClearAsync();

        await Context.Users.AddAsync(AdminsData.MainAdmin);
        await SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        _settings.Mfa.BypassForTesting = true;
        _settings.Mfa.MaxConcurrentSessions = 5;
        _settings.Mfa.SessionRetentionDays = 30;

        InMemoryNewSignInEmailService.Clear();

        await ClearAsync();

        AuthenticateAsAdmin();
    }

    private async Task ClearAsync()
    {
        Context.MfaSessions.RemoveRange(Context.MfaSessions);
        Context.UserMfas.RemoveRange(Context.UserMfas);
        Context.AuditLogs.RemoveRange(Context.AuditLogs);
        Context.OrganisationUsers.RemoveRange(Context.OrganisationUsers);
        Context.Users.RemoveRange(Context.Users);
        Context.Organisations.RemoveRange(Context.Organisations);
        await SaveChangesAsync();
    }
}