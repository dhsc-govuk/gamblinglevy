using Application.Common.Interfaces;
using Domain.Organisations;
using Domain.OrganisationUsers;
using Domain.Users;
using Domain.ValueObjects;
using FluentAssertions;
using LanguageExt.UnsafeValueAccess;
using Microsoft.Extensions.DependencyInjection;
using Tests.Common;
using Tests.Data;
using Xunit;

namespace Api.Tests.Integration.Users;

// Covers IUserProfileRepository directly rather than over HTTP, because this is the one component the rest
// of the suite cannot reach: TestAuthHandler injects claims straight into the principal, so production's
// ApplicationUserClaimsTransformation — which calls GetUserProfile and turns its result into the role /
// userId / organisationId claims — is never exercised by a controller test.
//
// That blind spot is exactly how BLUM-7038 shipped: GetUserProfile handled admin / organisation admin /
// location admin only, so a Lead resolved to None, received no claims at all, and was rejected by every
// [Authorize(Roles = ...)] despite appearing in all of them. A Lead who cannot resolve a profile cannot
// use the service, so these assertions are the real contract behind "LA Lead works".
public class UserProfileLookupTests(IntegrationTestWebFactory factory)
    : BaseIntegrationTest(factory), IAsyncLifetime
{
    private readonly IServiceScopeFactory _scopeFactory =
        factory.Services.GetRequiredService<IServiceScopeFactory>();

    private readonly Organisation _organisation = OrganisationsData.MainOrganisation;

    [Fact]
    public async Task ShouldResolveProfileForLead_WithTheirOrganisationId()
    {
        var lead = SeedableUser("la.lead@org.com", UserRole.Lead);
        await AddUserWithMembershipAsync(lead);

        var profile = await GetUserProfileAsync(lead.SubId);

        profile.IsSome.Should().BeTrue(
            "a Lead with an organisation_users row must resolve a profile — without one the claims "
            + "transformation adds no claims and every [Authorize(Roles = ...)] rejects them");
        var resolved = profile.ValueUnsafe();
        resolved.Role.Should().Be(UserRole.Lead);
        resolved.Id.Should().Be(lead.Id);
        resolved.OrganisationId.IsSome.Should().BeTrue();
        resolved.OrganisationId.ValueUnsafe().Should().Be(_organisation.Id);
    }

    [Fact]
    public async Task ShouldResolveProfileForOrganisationAdmin_WithTheirOrganisationId()
    {
        var orgAdmin = SeedableUser("la.admin@org.com", UserRole.OrganisationAdmin);
        await AddUserWithMembershipAsync(orgAdmin);

        var profile = await GetUserProfileAsync(orgAdmin.SubId);

        profile.IsSome.Should().BeTrue();
        var resolved = profile.ValueUnsafe();
        resolved.Role.Should().Be(UserRole.OrganisationAdmin);
        resolved.OrganisationId.ValueUnsafe().Should().Be(_organisation.Id);
    }

    [Fact]
    public async Task ShouldResolveProfileForAdmin_WithoutAnOrganisation()
    {
        var admin = SeedableUser("dhsc.admin@dhsc.gov.uk", UserRole.Admin);
        await Context.Users.AddAsync(admin);
        await SaveChangesAsync();

        var profile = await GetUserProfileAsync(admin.SubId);

        profile.IsSome.Should().BeTrue();
        var resolved = profile.ValueUnsafe();
        resolved.Role.Should().Be(UserRole.Admin);
        resolved.OrganisationId.IsNone.Should().BeTrue("DHSC admins are not scoped to an organisation");
    }

    [Fact]
    public async Task ShouldNotResolveProfileForLead_WithoutAnOrganisationMembership()
    {
        // The orphan case the BLUM-7038 migration reports rather than repairs: an LA role with no
        // organisation_users row cannot be scoped to an organisation, so it resolves to None and the user
        // is refused. Asserted so the behaviour is a deliberate, visible refusal rather than an accident.
        var orphan = SeedableUser("orphan.lead@org.com", UserRole.Lead);
        await Context.Users.AddAsync(orphan);
        await SaveChangesAsync();

        var profile = await GetUserProfileAsync(orphan.SubId);

        profile.IsNone.Should().BeTrue();
    }

    [Fact]
    public async Task ShouldNotResolveProfileForAnUnknownSubId()
    {
        var profile = await GetUserProfileAsync(new SubId(Guid.NewGuid()));

        profile.IsNone.Should().BeTrue();
    }

    private async Task<LanguageExt.Option<Domain.UserProfile>> GetUserProfileAsync(SubId subId)
    {
        // A fresh scope per call: UserProfileRepository owns its ApplicationDbContext and disposes it.
        // The scope must be async — the repository is IAsyncDisposable only, so a synchronous Dispose throws.
        await using var scope = _scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IUserProfileRepository>();
        return await repository.GetUserProfile(subId);
    }

    private static User SeedableUser(string email, UserRole role) => User.New(
        id: UserId.New(),
        name: new Name("profile", "lookup"),
        email: email,
        new SubId(Guid.NewGuid()),
        status: UserStatus.Active,
        role: role);

    private async Task AddUserWithMembershipAsync(User user)
    {
        var membership = OrganisationUser.New(
            id: new OrganisationUserId(user.Id.Value),
            userId: user.Id,
            organisationId: _organisation.Id,
            user: user);

        await Context.OrganisationUsers.AddAsync(membership);
        await SaveChangesAsync();
    }

    public async Task InitializeAsync()
    {
        await Context.Organisations.AddAsync(_organisation);
        await SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        Context.OrganisationUsers.RemoveRange(Context.OrganisationUsers);
        Context.Users.RemoveRange(Context.Users);
        Context.Organisations.RemoveRange(Context.Organisations);
        await SaveChangesAsync();
        AuthenticateAsAdmin();
    }
}