using System.Net;
using System.Text;
using Application.Common.Interfaces;
using Domain.Users;
using Domain.ValueObjects;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tests.Common;
using Tests.Data;
using Xunit;

namespace Api.Tests.Integration.Users;

// The refusal a deactivated user actually receives (BLUM-6386). Before this middleware the answer was
// [Authorize]'s bodiless 403, which the SPA's interceptor cannot tell apart from a service fault — so the
// sign-in page showed its generic "we cannot sign you in ... tell your administrator" banner for an account
// that had been switched off deliberately.
public class UserDeactivatedMiddlewareTests(IntegrationTestWebFactory factory)
    : BaseIntegrationTest(factory), IAsyncLifetime
{
    private const string DeactivatedErrorCode = "USER_DEACTIVATED";

    private readonly IServiceScopeFactory _scopeFactory =
        factory.Services.GetRequiredService<IServiceScopeFactory>();

    // A user who is deactivated in the database, reached through production's claims transformation rather
    // than through synthesised claims — see ShouldRefuseADeactivatedUser_ThroughTheRealClaimsPipeline.
    private readonly User _deactivated = User.New(
        id: UserId.New(),
        name: new Name("Deactivated", "Subject"),
        email: "deactivated.subject@dhsc.gov.uk",
        subId: new SubId(Guid.NewGuid()),
        status: UserStatus.Deactivated,
        role: UserRole.Admin,
        utcNow: DateTime.UtcNow);

    [Fact]
    public async Task ShouldRefuseADeactivatedUser_WithTheUserDeactivatedEnvelope()
    {
        AuthenticateAsDeactivated(AdminsData.MainAdmin.Id.Value);

        var response = await Client.GetAsync("admin/users");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain(DeactivatedErrorCode, "the code is the contract client/src/helpers/authFailure.ts "
            + "matches on; without it the SPA falls back to its generic service-fault banner");
    }

    // The test the previous version of this class could not write. Everything else here synthesises the
    // status claim in TestAuthHandler, which proves the middleware reads a claim but not that anything ever
    // sets it. This one seeds a deactivated row and emits only an object id, so ApplicationUserClaimsTransformation
    // resolves the profile and decides the claims exactly as it does behind a real CIAM token — covering the
    // transformation, its registration, and the middleware's position ahead of UseAuthorization in one pass.
    [Fact]
    public async Task ShouldRefuseADeactivatedUser_ThroughTheRealClaimsPipeline()
    {
        await Context.Users.AddAsync(_deactivated);
        await SaveChangesAsync();
        InvalidateClaimsCache(_deactivated.SubId);

        AuthenticateThroughClaimsTransformation(_deactivated.SubId.Value);

        // Twice, for the reason DeactivatedUserClaimsTests loops: the first request populates the claims
        // cache and the second is served from it, and those were the two paths that used to disagree.
        var first = await Client.GetAsync("admin/users");
        var second = await Client.GetAsync("admin/users");

        foreach (var response in new[] { first, second })
        {
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await response.Content.ReadAsStringAsync()).Should().Contain(DeactivatedErrorCode);
        }
    }

    [Fact]
    public async Task ShouldStillServeAnActiveUser_ThroughTheRealClaimsPipeline()
    {
        var active = User.New(
            id: UserId.New(),
            name: new Name("Active", "Subject"),
            email: "active.subject@dhsc.gov.uk",
            subId: new SubId(Guid.NewGuid()),
            status: UserStatus.Active,
            role: UserRole.Admin,
            utcNow: DateTime.UtcNow);

        await Context.Users.AddAsync(active);
        await SaveChangesAsync();
        InvalidateClaimsCache(active.SubId);

        AuthenticateThroughClaimsTransformation(active.SubId.Value);

        var response = await Client.GetAsync("admin/users");

        response.IsSuccessStatusCode.Should().BeTrue(
            "an active admin resolved through the same pipeline must still reach a role-gated endpoint — "
            + "otherwise this class would pass even if the transformation stopped issuing claims entirely");
    }

    [Fact]
    public async Task ShouldRefuseADeactivatedUser_OnTheMfaEndpoints()
    {
        // /mfa/* is exempt from MfaRequiredMiddleware but pointedly not from this one. VerifyMfaCommand
        // checks enrolment, lockout and the code, never the user's status, so an unexempt /mfa/verify would
        // let a deactivated user holding a live access token mint a fresh session.
        AuthenticateAsDeactivated(AdminsData.MainAdmin.Id.Value);

        var response = await Client.PostAsync("mfa/verify", JsonContent("{\"code\":\"123456\"}"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Contain(DeactivatedErrorCode);
    }

    [Fact]
    public async Task ShouldLetADeactivatedUserSignOut()
    {
        // /mfa/logout is exempt for the reason recorded on OrganisationSuspendedMiddleware: once the SPA has
        // been sent to /account-deactivated the interceptor hangs any further USER_DEACTIVATED response on a
        // promise that never settles, so blocking logout would leave that page's Sign out button stuck
        // (BLUM-7242).
        //
        // Driven through the real claims pipeline, and asserted on the status code rather than just the
        // absence of the envelope. Both matter: the exemption was silently useless at first because
        // ApplyClaims withheld the userId claim, so MfaController.Logout could not resolve the caller and
        // answered 401 — and the original assertion checked only that the body lacked USER_DEACTIVATED,
        // which a 401 body satisfies. Synthesising the claims here would not catch it either, since then the
        // test supplies the very claim production was failing to emit.
        await Context.Users.AddAsync(_deactivated);
        await SaveChangesAsync();
        InvalidateClaimsCache(_deactivated.SubId);

        AuthenticateThroughClaimsTransformation(_deactivated.SubId.Value);

        var response = await Client.PostAsync("mfa/logout", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task ShouldNotBlockTheHealthProbe_ForADeactivatedUser()
    {
        AuthenticateAsDeactivated(AdminsData.MainAdmin.Id.Value);

        var response = await Client.GetAsync("health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("healthcheck-details")]
    [InlineData("mfa/logout-everywhere")]
    [InlineData("swagger-admin")]
    public async Task ShouldNotTreatAPathThatMerelyStartsWithAnExemptEntryAsExempt(string path)
    {
        // The exempt list is matched on whole path segments, not as a bare string prefix. Under a raw
        // StartsWith every route below would have been exempt from the gate — "/health" would have covered
        // healthcheck-details and "/mfa/logout" an /mfa/logout-everywhere — which reads as three entries but
        // means "any route beginning with these letters". CLAUDE.md records the same class of bug for
        // RateLimitPolicyResolver, where it silently disabled a whole control.
        //
        // None of these routes exists, so the gate answering first is the whole assertion: a 404 would mean
        // the request had been let through to routing.
        AuthenticateAsDeactivated(AdminsData.MainAdmin.Id.Value);

        var response = await Client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Contain(DeactivatedErrorCode);
    }

    [Fact]
    public async Task ShouldLeaveAnActiveUserAlone()
    {
        var response = await Client.GetAsync("admin/users");

        response.IsSuccessStatusCode.Should().BeTrue();
    }

    // The claims cache is a singleton on the shared test host, so an entry written by one test would
    // otherwise decide what a later one sees.
    private void InvalidateClaimsCache(SubId subId)
    {
        using var scope = _scopeFactory.CreateScope();
        scope.ServiceProvider.GetRequiredService<IUserClaimsCache>().Invalidate(subId);
    }

    private static StringContent JsonContent(string json) => new(json, Encoding.UTF8, "application/json");

    public Task InitializeAsync()
    {
        AuthenticateAsAdmin();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        AuthenticateAsAdmin();
        Context.Users.RemoveRange(Context.Users.Where(x => x.Email.EndsWith(".subject@dhsc.gov.uk")));
        await SaveChangesAsync();
    }
}