using System.Security.Claims;
using Api.Modules;
using Application.Common.Interfaces;
using Domain;
using Domain.Users;
using Domain.ValueObjects;
using FluentAssertions;
using Infrastructure.Caching;
using LanguageExt;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Identity.Web;
using Xunit;

namespace Api.Tests.Integration.Users;

// Covers ApplicationUserClaimsTransformation directly. Deliberately not a BaseIntegrationTest: the class
// takes only IInMemoryCache and IUserProfileRepository, so it needs neither the API host nor a PostgreSQL
// database, and every test class that inherits BaseIntegrationTest costs a full app boot plus a database of its
// own (migrated and seeded from empty).
//
// It is also the only way to reach this code at all. TestAuthHandler injects claims straight into the
// principal, so no controller test in the suite ever runs the transformation — which is how BLUM-6386's
// leak survived: the deactivated guard lived on the cache-miss path only, while the miss path deliberately
// cached the profile it had just refused. The hit path then handed that same profile to SetUserClaims
// unconditionally, so the first request after deactivation was blocked and every request for the next five
// minutes carried full claims, repeating on each TTL.
//
// Which is why the assertions below call TransformAsync twice. A single-call test passes against the
// broken version.
public class DeactivatedUserClaimsTests
{
    private static readonly SubId Subject = new(Guid.NewGuid());
    private static readonly UserId SubjectUserId = UserId.New();

    [Fact]
    public async Task ShouldWithholdTheRoleClaimFromADeactivatedUser_OnEveryRequestNotJustTheFirst()
    {
        var transformation = BuildTransformation(UserStatus.Deactivated, out var repository);

        var first = await transformation.TransformAsync(NewPrincipal());
        var second = await transformation.TransformAsync(NewPrincipal());

        repository.Calls.Should().Be(1, "the second request must be served from the cache — otherwise this "
            + "test is not exercising the cache-hit path the leak lived on");

        foreach (var principal in new[] { first, second })
        {
            principal.FindFirstValue(ClaimConstants.Role).Should().BeNull(
                "the missing role claim is the fail-closed half of the block — every [Authorize(Roles = ...)] "
                + "must reject a deactivated user even if UserDeactivatedMiddleware were removed");
            principal.FindFirstValue("organisationId").Should().BeNull(
                "nothing should be able to scope a query to an organisation on a deactivated user's behalf");
        }
    }

    [Fact]
    public async Task ShouldGiveADeactivatedUserStatusAndUserId_AndNothingElse()
    {
        var transformation = BuildTransformation(UserStatus.Deactivated, out _);

        var first = await transformation.TransformAsync(NewPrincipal());
        var second = await transformation.TransformAsync(NewPrincipal());

        foreach (var principal in new[] { first, second })
        {
            // Without status the request is still refused, but by [Authorize] with an empty body — which the
            // SPA cannot tell apart from a broken service. It is the claim, not the refusal, that BLUM-6386 adds.
            principal.FindFirstValue("status").Should().Be(Status.Deactivated);

            // userId exists for exactly one caller: MfaController.Logout, the only endpoint the middleware
            // exempts. Without it the exemption is useless — logout cannot resolve who is signing out and
            // answers 401, so the session row survives and the SPA reports an exception on every sign-out.
            principal.FindFirstValue("userId").Should().Be(SubjectUserId.Value.ToString());

            var granted = principal.Claims
                .Select(x => x.Type)
                .Where(type => type != ClaimConstants.ObjectId);

            granted.Should().BeEquivalentTo(
                ["status", "userId"],
                "any further claim widens what a deactivated principal can reach");
        }
    }

    // Every claim in ApplyClaims is guarded on not already being present, so a second pass over the same
    // principal has nothing to add — but it used to append the empty ClaimsIdentity anyway. Production runs
    // the transformation once per request against the principal the JWT handler just built, so nothing does
    // that today; a policy naming an authentication scheme (a second AuthenticateAsync) would. The point is
    // that "status and userId, and nothing else" is an invariant of the guards, not of how many times the
    // transformation happens to run.
    [Fact]
    public async Task ShouldAddNothing_OnASecondPassOverTheSamePrincipal()
    {
        var transformation = BuildTransformation(UserStatus.Deactivated, out _);
        var principal = NewPrincipal();

        await transformation.TransformAsync(principal);
        var identitiesAfterFirstPass = principal.Identities.Count();

        await transformation.TransformAsync(principal);

        principal.Identities.Should().HaveCount(
            identitiesAfterFirstPass,
            "a pass with no claim to add must not append an empty identity");
        principal.Claims
            .Select(x => x.Type)
            .Where(type => type != ClaimConstants.ObjectId)
            .Should().BeEquivalentTo(["status", "userId"]);
    }

    [Fact]
    public async Task ShouldStillIssueClaimsToAnActiveUser_OnBothCachePaths()
    {
        var transformation = BuildTransformation(UserStatus.Active, out _);

        var first = await transformation.TransformAsync(NewPrincipal());
        var second = await transformation.TransformAsync(NewPrincipal());

        first.FindFirstValue(ClaimConstants.Role).Should().Be(Role.Admin);
        second.FindFirstValue(ClaimConstants.Role).Should().Be(Role.Admin);
        second.FindFirstValue("status").Should().Be(Status.Active);
    }

    private static ApplicationUserClaimsTransformation BuildTransformation(
        UserStatus status,
        out CountingUserProfileRepository repository)
    {
        var user = User.New(
            id: SubjectUserId,
            name: new Name("Claims", "Subject"),
            email: "claims.subject@dhsc.gov.uk",
            subId: Subject,
            status: status,
            role: UserRole.Admin,
            utcNow: DateTime.UtcNow);

        repository = new CountingUserProfileRepository(UserProfile.NewAdminProfile(user));

        return new ApplicationUserClaimsTransformation(
            new InMemoryCache(new MemoryCache(new MemoryCacheOptions())),
            repository);
    }

    // A fresh principal per call, mirroring production: IClaimsTransformation runs against the principal the
    // JWT handler just built, never one a previous request already decorated.
    private static ClaimsPrincipal NewPrincipal()
    {
        return new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimConstants.ObjectId, Subject.Value.ToString())],
            "Test"));
    }

    private sealed class CountingUserProfileRepository(UserProfile profile) : IUserProfileRepository
    {
        public int Calls { get; private set; }

        public Task<Option<UserProfile>> GetUserProfile(SubId subId, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult<Option<UserProfile>>(profile);
        }
    }
}