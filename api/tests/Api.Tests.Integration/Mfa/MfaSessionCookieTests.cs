using System.Net;
using System.Net.Http.Json;
using Application.Common.Settings;
using Application.Mfa.Dtos;
using Domain.Users;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using OtpNet;
using Tests.Common;
using Tests.Data;
using Xunit;

namespace Api.Tests.Integration.Mfa;

/// <summary>
/// BLUM-8419 — the flags mfa_session is written and deleted with.
///
/// Where the client and the API are on different sites (our Azure environments: two *.azurewebsites.net
/// hosts, and azurewebsites.net is on the Public Suffix List) the cookie is a third-party one, and a browser
/// that blocks those — Chrome Incognito and Edge InPrivate do by default — discards it. The code is accepted,
/// the session row is written, and the very next request arrives with no cookie. A cookie marked Partitioned
/// (CHIPS) is still stored there, so a cross-site request is given one alongside the original.
///
/// Nothing asserted the flags before this, which is how the cookie could be unusable in a whole browser mode
/// with every test green: the suite hands the session id back in a hand-written Cookie header and never
/// looks at what the server asked the browser to store.
///
/// Runs with the MFA gate ON, like the other session classes, because three of the six places that delete
/// the cookie are inside MfaRequiredMiddleware. BaseIntegrationTest is IClassFixture-scoped, so the settings
/// singleton belongs to this class alone and flipping it cannot leak into a class running in parallel.
/// </summary>
public class MfaSessionCookieTests : BaseIntegrationTest, IAsyncLifetime
{
    // Outside the middleware's "/mfa/", "/swagger", "/health" exemptions, takes no route parameters, and the
    // default test identity is a platform admin.
    private const string ProtectedRoute = "admin/users";
    private const string MfaSessionCookieName = "mfa_session";

    // What a browser sends on a request from one site to another. It is a forbidden header name — page
    // script cannot set or remove it — which is what makes it usable as the signal.
    private const string FetchSiteHeader = "Sec-Fetch-Site";
    private const string CrossSite = "cross-site";

    // The session binds to the User-Agent (and optionally the IP), so every request presents the same pair.
    private const string ClientIp = "203.0.113.84";

    private readonly ApplicationSettings _settings;

    // Captured from enrolment so later sign-ins can mint fresh codes for the same authenticator.
    private string _authenticatorSecret = string.Empty;
    private List<string> _recoveryCodes = [];

    public MfaSessionCookieTests(IntegrationTestWebFactory factory)
        : base(factory)
    {
        _settings = factory.Services.GetRequiredService<ApplicationSettings>();
    }

    [Theory]
    [InlineData("mfa/setup/verify")]
    [InlineData("mfa/verify")]
    [InlineData("mfa/verify/recovery")]
    public async Task ShouldIssueBothCookieShapes_WhenTheRequestIsCrossSite(string route)
    {
        var (response, sessionId) = await IssueSessionAsync(route, CrossSite);

        // Both, with one value, because no single shape survives everywhere a cross-site request comes from.
        // A browser blocking third-party cookies keeps only the partitioned one. Safari 18.5 to 26.1 is
        // reported to drop a cookie carrying the attribute, so a Safari user who has allowed cross-site
        // cookies keeps only the unpartitioned one — which is also what overwrites a cookie left by a build
        // from before the partitioned shape existed.
        var cookies = SessionCookies(response);
        cookies.Should().HaveCount(2);
        cookies.Should().ContainSingle(x => IsPartitioned(x));
        cookies.Should().ContainSingle(x => !IsPartitioned(x));

        foreach (var cookie in cookies)
        {
            cookie.Value.ToString().Should().Be(sessionId.ToString());
            cookie.Expires.Should().NotBeNull().And.BeAfter(DateTimeOffset.UtcNow);
            AssertSessionFlags(cookie);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("same-origin")]
    [InlineData("same-site")]
    [InlineData("none")]
    [InlineData("not-a-defined-value")]
    [InlineData("cross-site, same-origin")]
    public async Task ShouldIssueTheUnpartitionedCookie_WhenTheRequestIsNotCrossSite(string? fetchSite)
    {
        // Where the API is served from the client's own site the cookie is first-party and already works
        // everywhere, so it is left exactly as it was: Safari 18.5 to 26.1 is reported to drop a cookie that
        // carries Partitioned even in a first-party context. A request with no header at all — an older
        // browser, or a proxy that strips it — gets the same, and so does anything a browser would not send.
        var (response, sessionId) = await IssueSessionAsync("mfa/verify", fetchSite);

        var cookie = SessionCookies(response).Should().ContainSingle().Subject;
        cookie.Value.ToString().Should().Be(sessionId.ToString());
        cookie.Expires.Should().NotBeNull().And.BeAfter(DateTimeOffset.UtcNow);
        IsPartitioned(cookie).Should().BeFalse();
        AssertSessionFlags(cookie);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ShouldAcceptEitherCookieIssuedToACrossSiteRequest_OnItsOwn(bool partitioned)
    {
        // A browser keeps whichever of the two it accepts, so each has to carry the session by itself. The
        // value sent back is the one in that Set-Cookie line — what the browser would store — and not the
        // SessionId in the response body, which would pass whatever the server had asked it to store.
        var (issue, _) = await IssueSessionAsync("mfa/verify", CrossSite);
        var kept = SessionCookies(issue).Single(x => IsPartitioned(x) == partitioned);

        var request = new HttpRequestMessage(HttpMethod.Get, ProtectedRoute);
        request.Headers.Add("X-Azure-ClientIP", ClientIp);
        request.Headers.Add(FetchSiteHeader, CrossSite);
        request.Headers.Add("Cookie", $"{kept.Name}={kept.Value}");

        var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ShouldDeleteBothCookieShapes_OnLogout()
    {
        var sessionId = await EnrolAsync();

        var response = await SendAsync(HttpMethod.Post, "mfa/logout", sessionId);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        AssertDeletesBothShapes(response);
    }

    [Fact]
    public async Task ShouldDeleteBothCookieShapes_WhenMfaIsDisabled()
    {
        var sessionId = await EnrolAsync();

        var response = await SendAsync(
            HttpMethod.Post,
            "mfa/disable",
            sessionId,
            new MfaVerifyRequestDto(GenerateTotpCode(_authenticatorSecret)));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        AssertDeletesBothShapes(response);
    }

    [Fact]
    public async Task ShouldDeleteBothCookieShapes_WhenTheCallerRevokesTheirOwnSession()
    {
        var sessionId = await EnrolAsync();

        var response = await SendAsync(HttpMethod.Post, $"profile/sessions/{sessionId}/revoke", sessionId);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        AssertDeletesBothShapes(response);
    }

    [Theory]
    [InlineData("SESSION_IDLE_TIMEOUT")]
    [InlineData("SESSION_EXPIRED")]
    [InlineData("SESSION_REVOKED")]
    public async Task ShouldDeleteBothCookieShapes_WhenTheSessionHasEnded(string expectedError)
    {
        // The client answers these by ending the CIAM sign-in, so nothing overwrites the cookie afterwards.
        // A deletion that names only one shape leaves the other in the browser, which presents it on the next
        // sign-in, is told the session is gone, and is signed straight back out — for up to eight hours.
        var sessionId = await EnrolAsync();
        await EndSessionAsync(sessionId, expectedError);

        var response = await SendAsync(HttpMethod.Get, ProtectedRoute, sessionId);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync()).Should().Contain(expectedError);
        AssertDeletesBothShapes(response);
    }

    [Fact]
    public async Task ShouldReadTheLastCookie_WhenTheRequestCarriesTheNameTwice()
    {
        // A partitioned and an unpartitioned mfa_session are two cookies to the browser, and it will send
        // both if it has both. They are written together with one value, so they normally agree; where they
        // do not, ASP.NET keeps the last value for a repeated name. This pins that, so a change of framework
        // behaviour shows up here rather than as users being asked for a code they have just entered.
        var sessionId = await EnrolAsync();

        var request = new HttpRequestMessage(HttpMethod.Get, ProtectedRoute);
        request.Headers.Add("X-Azure-ClientIP", ClientIp);
        request.Headers.Add("Cookie", $"{MfaSessionCookieName}={Guid.NewGuid()}; {MfaSessionCookieName}={sessionId}");

        var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // Deletion is unconditional — no Sec-Fetch-Site on any of the requests above — because which shape the
    // browser holds was decided when the cookie was issued, not by the request that ends the session.
    private static void AssertDeletesBothShapes(HttpResponseMessage response)
    {
        var cookies = SessionCookies(response);

        cookies.Should().HaveCount(2);
        cookies.Should().ContainSingle(x => IsPartitioned(x));
        cookies.Should().ContainSingle(x => !IsPartitioned(x));

        foreach (var cookie in cookies)
        {
            cookie.Value.ToString().Should().BeEmpty();
            cookie.Expires.Should().NotBeNull().And.BeBefore(DateTimeOffset.UtcNow);
            AssertSessionFlags(cookie);
        }
    }

    // The flags both shapes share, on the way in and on the way out. A deletion that differs from the cookie
    // it targets in any of these is a different cookie to the browser and deletes nothing.
    private static void AssertSessionFlags(SetCookieHeaderValue cookie)
    {
        cookie.HttpOnly.Should().BeTrue();
        cookie.Secure.Should().BeTrue();
        cookie.SameSite.Should().Be(SameSiteMode.None);
        cookie.Path.ToString().Should().Be("/");

        // Host-only. A Domain would widen the cookie to sibling hosts under a shared suffix.
        cookie.Domain.HasValue.Should().BeFalse();
    }

    private static bool IsPartitioned(SetCookieHeaderValue cookie) =>
        cookie.Extensions.Any(x => x.Equals("Partitioned", StringComparison.OrdinalIgnoreCase));

    private static List<SetCookieHeaderValue> SessionCookies(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            return [];
        }

        return SetCookieHeaderValue.ParseList(values.ToList())
            .Where(x => x.Name.Equals(MfaSessionCookieName, StringComparison.Ordinal))
            .ToList();
    }

    // Enrols first where the route under test is not the enrolment itself, then calls it with the given
    // Sec-Fetch-Site. Re-using a TOTP code inside its 30-second step is fine: only failures are counted.
    private async Task<(HttpResponseMessage Response, Guid SessionId)> IssueSessionAsync(string route, string? fetchSite)
    {
        if (route == "mfa/setup/verify")
        {
            var setupResponse = await SendAsync(HttpMethod.Post, "mfa/setup", sessionId: null);
            var setup = await setupResponse.ToResponseModel<MfaSetupResponseDto>();
            _authenticatorSecret = setup.ManualEntryKey;

            var setupVerify = await SendAsync(
                HttpMethod.Post,
                route,
                sessionId: null,
                new MfaVerifyRequestDto(GenerateTotpCode(_authenticatorSecret)),
                fetchSite);
            setupVerify.IsSuccessStatusCode.Should().BeTrue();

            return (setupVerify, (await setupVerify.ToResponseModel<MfaSetupCompleteResponseDto>()).SessionId);
        }

        await EnrolAsync();

        var code = route == "mfa/verify/recovery" ? _recoveryCodes[0] : GenerateTotpCode(_authenticatorSecret);
        var response = await SendAsync(HttpMethod.Post, route, sessionId: null, new MfaVerifyRequestDto(code), fetchSite);
        response.IsSuccessStatusCode.Should().BeTrue();

        return (response, (await response.ToResponseModel<MfaVerifyResponseDto>()).SessionId);
    }

    private async Task<Guid> EnrolAsync()
    {
        var setupResponse = await SendAsync(HttpMethod.Post, "mfa/setup", sessionId: null);
        var setup = await setupResponse.ToResponseModel<MfaSetupResponseDto>();
        _authenticatorSecret = setup.ManualEntryKey;

        var verifyResponse = await SendAsync(
            HttpMethod.Post,
            "mfa/setup/verify",
            sessionId: null,
            new MfaVerifyRequestDto(GenerateTotpCode(_authenticatorSecret)));
        verifyResponse.IsSuccessStatusCode.Should().BeTrue();

        var complete = await verifyResponse.ToResponseModel<MfaSetupCompleteResponseDto>();
        _recoveryCodes = complete.RecoveryCodes;

        return complete.SessionId;
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string route,
        Guid? sessionId,
        object? body = null,
        string? fetchSite = null)
    {
        var request = new HttpRequestMessage(method, route);
        request.Headers.Add("X-Azure-ClientIP", ClientIp);

        if (fetchSite is not null)
        {
            request.Headers.Add(FetchSiteHeader, fetchSite);
        }

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

    // Through the database rather than the domain: MfaSession deliberately exposes no setter for its
    // timestamps, and a test-only one would be a hole in the aggregate.
    private async Task EndSessionAsync(Guid sessionId, string error)
    {
        var session = Context.MfaSessions.Where(x => x.Id == new MfaSessionId(sessionId));

        switch (error)
        {
            case "SESSION_IDLE_TIMEOUT":
                var staleActivity = DateTime.UtcNow.AddMinutes(-_settings.Mfa.IdleTimeoutMinutes - 1);
                await session.ExecuteUpdateAsync(x => x.SetProperty(s => s.LastActivityAt, staleActivity));
                break;

            case "SESSION_EXPIRED":
                var pastExpiry = DateTime.UtcNow.AddMinutes(-1);
                await session.ExecuteUpdateAsync(x => x.SetProperty(s => s.ExpiresAt, pastExpiry));
                break;

            case "SESSION_REVOKED":
                await session.ExecuteUpdateAsync(x => x.SetProperty(s => s.IsRevoked, true));
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(error), error, "Not an ended-session envelope.");
        }

        Context.ChangeTracker.Clear();
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

        await ClearAsync();

        await Context.Users.AddAsync(AdminsData.MainAdmin);
        await SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        _settings.Mfa.BypassForTesting = true;

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