using System.Net;
using System.Text.Json;
using Api.Dtos;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.Organisations.Commands.SuspendLocalAuthority;
using Application.Users;
using Domain.AuditLogs;
using Domain.Organisations;
using Domain.OrganisationUsers;
using Domain.Users;
using Domain.ValueObjects;
using FluentAssertions;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tests.Common;
using Tests.Data;
using Xunit;

namespace Api.Tests.Integration.Organisations;

public class LocalAuthoritySuspendReinstateTests(IntegrationTestWebFactory factory)
    : BaseIntegrationTest(factory), IAsyncLifetime
{
    private readonly Organisation _active = Organisation.New(
        OrganisationId.New(),
        "Active Council",
        "E06000060",
        Region.London,
        OrgType.LocalAuthority,
        "E1000001",
        OnboardingStatus.Active,
        DateTime.UtcNow);

    private readonly Organisation _invited = Organisation.New(
        OrganisationId.New(),
        "Invited Council",
        "E06000061",
        Region.London,
        OrgType.LocalAuthority,
        "E1000001",
        OnboardingStatus.Invited,
        DateTime.UtcNow);

    private readonly Organisation _suspended = Organisation.New(
        OrganisationId.New(),
        "Suspended Council",
        "E06000062",
        Region.London,
        OrgType.LocalAuthority,
        "E1000001",
        OnboardingStatus.Suspended,
        DateTime.UtcNow);

    private readonly Organisation _orgWithUser = Organisation.New(
        OrganisationId.New(),
        "Council With User",
        "E06000063",
        Region.London,
        OrgType.LocalAuthority,
        "E1000001",
        OnboardingStatus.Active,
        DateTime.UtcNow);

    // An organisation nobody deactivates, so its user proves the cascade stays inside the one being deactivated.
    private readonly Organisation _neighbour = Organisation.New(
        OrganisationId.New(),
        "Neighbouring Council",
        "E06000064",
        Region.London,
        OrgType.LocalAuthority,
        "E1000001",
        OnboardingStatus.Active,
        DateTime.UtcNow);

    private readonly Organisation _vcse = Organisation.New(
        OrganisationId.New(),
        "VCSE Partner",
        "E06000065",
        Region.London,
        OrgType.Vcse,
        "E1000001",
        OnboardingStatus.Active,
        DateTime.UtcNow);

    private readonly IServiceScopeFactory _scopeFactory = factory.Services.GetRequiredService<IServiceScopeFactory>();

    private OrganisationUser _orgUser = null!;
    private OrganisationUser _lead = null!;
    private OrganisationUser _alreadyDeactivated = null!;
    private OrganisationUser _neighbourUser = null!;
    private OrganisationUser _vcseUser = null!;

    [Fact]
    public async Task ShouldSuspend_ActiveLA_AndWriteAuditLog()
    {
        var response = await Client.PostAsync($"organisations/local-authorities/{_active.Id.Value}/suspend", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.ToResponseModel<LocalAuthorityDto>();
        dto.OnboardingStatus.Should().Be(OnboardingStatuses.Suspended);

        var saved = await Context.Organisations.AsNoTracking().FirstAsync(x => x.Id == _active.Id);
        saved.OnboardingStatus.ToString().Should().Be(OnboardingStatuses.Suspended);

        var audit = await Context.AuditLogs.AsNoTracking()
            .Where(x => x.TargetType == AuditTargetType.Organisation && x.TargetId == _active.Id.Value)
            .ToListAsync();
        audit.Should().ContainSingle(x => x.Action == AuditAction.OrganisationSuspended);
    }

    [Fact]
    public async Task ShouldSuspend_InvitedLA()
    {
        var response = await Client.PostAsync($"organisations/local-authorities/{_invited.Id.Value}/suspend", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var saved = await Context.Organisations.AsNoTracking().FirstAsync(x => x.Id == _invited.Id);
        saved.OnboardingStatus.ToString().Should().Be(OnboardingStatuses.Suspended);
    }

    [Fact]
    public async Task ShouldRejectSuspend_OfAlreadySuspendedLA()
    {
        var response = await Client.PostAsync($"organisations/local-authorities/{_suspended.Id.Value}/suspend", null);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task ShouldReturnNotFound_WhenSuspendingUnknownLA()
    {
        var response = await Client.PostAsync($"organisations/local-authorities/{Guid.NewGuid()}/suspend", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ShouldReinstate_SuspendedLA_AndWriteAuditLog()
    {
        var response = await Client.PostAsync($"organisations/local-authorities/{_suspended.Id.Value}/reinstate", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.ToResponseModel<LocalAuthorityDto>();
        dto.OnboardingStatus.Should().Be(OnboardingStatuses.Active);

        var saved = await Context.Organisations.AsNoTracking().FirstAsync(x => x.Id == _suspended.Id);
        saved.OnboardingStatus.ToString().Should().Be(OnboardingStatuses.Active);

        var audit = await Context.AuditLogs.AsNoTracking()
            .Where(x => x.TargetType == AuditTargetType.Organisation && x.TargetId == _suspended.Id.Value)
            .ToListAsync();
        audit.Should().ContainSingle(x => x.Action == AuditAction.OrganisationReinstated);
    }

    [Fact]
    public async Task ShouldRejectReinstate_OfActiveLA()
    {
        var response = await Client.PostAsync($"organisations/local-authorities/{_active.Id.Value}/reinstate", null);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task ShouldDeactivateEveryActiveUser_WhenOrganisationIsDeactivated()
    {
        var adminSession = await SeedSessionAsync(_orgUser.UserId);
        var leadSession = await SeedSessionAsync(_lead.UserId);

        var response = await Client.PostAsync($"organisations/local-authorities/{_orgWithUser.Id.Value}/suspend", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        (await GetUserAsync(_orgUser)).Status.Should().Be(UserStatus.Deactivated);
        (await GetUserAsync(_lead)).Status.Should().Be(UserStatus.Deactivated);

        // Already deactivated before the organisation was: not touched again, so UpdatedAt is still unset.
        var alreadyDeactivated = await GetUserAsync(_alreadyDeactivated);
        alreadyDeactivated.Status.Should().Be(UserStatus.Deactivated);
        alreadyDeactivated.UpdatedAt.Should().BeNull();

        (await GetUserAsync(_neighbourUser)).Status.Should().Be(UserStatus.Active);

        (await GetSessionAsync(adminSession)).IsRevoked.Should().BeTrue();
        (await GetSessionAsync(leadSession)).IsRevoked.Should().BeTrue();
    }

    [Fact]
    public async Task ShouldDeactivateUsers_WhenAVcseOrganisationIsDeactivated()
    {
        var response = await Client.PostAsync($"organisations/local-authorities/{_vcse.Id.Value}/suspend", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetUserAsync(_vcseUser)).Status.Should().Be(UserStatus.Deactivated);
    }

    [Fact]
    public async Task ShouldAuditEachUserDeactivation_AgainstTheOrganisationEvent()
    {
        var response = await Client.PostAsync($"organisations/local-authorities/{_orgWithUser.Id.Value}/suspend", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var userRows = await Context.AuditLogs.AsNoTracking()
            .Where(x => x.Action == AuditAction.UserDeactivated)
            .ToListAsync();

        // One per user the cascade deactivated, and none for the user who was already deactivated.
        userRows.Select(x => x.TargetId).Should().BeEquivalentTo(new[] { _orgUser.UserId.Value, _lead.UserId.Value });

        foreach (var row in userRows)
        {
            row.TargetType.Should().Be(AuditTargetType.User);
            row.ActorUserId.Should().Be(AdminsData.MainAdmin.Id);
            row.OrganisationId.Should().Be(_orgWithUser.Id);

            // jsonb reorders keys and whitespace, so read the payload rather than matching its text.
            using var before = JsonDocument.Parse(row.Before!);
            before.RootElement.GetProperty("status").GetString().Should().Be(Status.Active);

            using var after = JsonDocument.Parse(row.After!);
            after.RootElement.GetProperty("status").GetString().Should().Be(Status.Deactivated);
            var cause = after.RootElement.GetProperty("cause");
            cause.GetProperty("action").GetString().Should().Be(AuditAction.OrganisationSuspended);
            cause.GetProperty("organisationId").GetGuid().Should().Be(_orgWithUser.Id.Value);
        }

        var organisationRows = await Context.AuditLogs.AsNoTracking()
            .Where(x => x.TargetType == AuditTargetType.Organisation && x.TargetId == _orgWithUser.Id.Value)
            .ToListAsync();
        organisationRows.Should().ContainSingle(x => x.Action == AuditAction.OrganisationSuspended);
    }

    // Through production's claims transformation rather than a synthesised identity: TestAuthHandler never
    // reads a user's status from the database, so AuthenticateAs would still get through and be answered by
    // the organisation gate — a response a deactivated user can no longer receive.
    [Fact]
    public async Task ShouldDenyCascadedUser_ThroughTheRealClaimsPipeline()
    {
        // Warm the claims cache first, so the block depends on the cascade invalidating it.
        (await GetUsersAsMemberAsync(_orgUser)).StatusCode.Should().Be(HttpStatusCode.OK);

        AuthenticateAsAdmin();
        var suspend = await Client.PostAsync($"organisations/local-authorities/{_orgWithUser.Id.Value}/suspend", null);
        suspend.StatusCode.Should().Be(HttpStatusCode.OK);

        var after = await GetUsersAsMemberAsync(_orgUser);

        after.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorCodeAsync(after)).Should().Be("USER_DEACTIVATED");
    }

    [Fact]
    public async Task ShouldLeaveCascadedUsersDeactivated_WhenOrganisationIsReactivated()
    {
        var suspend = await Client.PostAsync($"organisations/local-authorities/{_orgWithUser.Id.Value}/suspend", null);
        suspend.StatusCode.Should().Be(HttpStatusCode.OK);

        var reinstate = await Client.PostAsync($"organisations/local-authorities/{_orgWithUser.Id.Value}/reinstate", null);
        reinstate.StatusCode.Should().Be(HttpStatusCode.OK);

        (await GetUserAsync(_orgUser)).Status.Should().Be(UserStatus.Deactivated);
        (await GetUserAsync(_lead)).Status.Should().Be(UserStatus.Deactivated);
        (await Context.AuditLogs.AsNoTracking().AnyAsync(x => x.Action == AuditAction.UserReactivated)).Should().BeFalse();

        var response = await GetUsersAsMemberAsync(_orgUser);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorCodeAsync(response)).Should().Be("USER_DEACTIVATED");
    }

    // Reactivating a user is its own action (AC4), and it does not lift the organisation's block: the only way
    // an organisation member now meets ORGANISATION_SUSPENDED is to be active inside a deactivated organisation.
    [Fact]
    public async Task ShouldKeepIndividuallyReactivatedUserBlocked_UntilOrganisationIsReactivated()
    {
        var suspend = await Client.PostAsync($"organisations/local-authorities/{_orgWithUser.Id.Value}/suspend", null);
        suspend.StatusCode.Should().Be(HttpStatusCode.OK);

        var reactivate = await Client.PostAsync($"organisations/{_orgWithUser.Id.Value}/users/{_orgUser.UserId.Value}/reactivate", null);
        reactivate.StatusCode.Should().Be(HttpStatusCode.OK);

        var whileSuspended = await GetUsersAsMemberAsync(_orgUser);
        whileSuspended.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorCodeAsync(whileSuspended)).Should().Be("ORGANISATION_SUSPENDED");

        AuthenticateAsAdmin();
        var reinstate = await Client.PostAsync($"organisations/local-authorities/{_orgWithUser.Id.Value}/reinstate", null);
        reinstate.StatusCode.Should().Be(HttpStatusCode.OK);

        (await GetUsersAsMemberAsync(_orgUser)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // A user who becomes active again around the cascade — an LA admin reactivates them, and they sign straight
    // back in — is left active on purpose (someone reactivated them), but must still be blocked by the
    // organisation once it is suspended. Whether they are depends on their cached claims and sessions being
    // cleared *after* the suspension commits. That timing cannot be staged over HTTP, so the handler is built by
    // hand with a claims cache that does the reactivation at the moment the Lead's claims are first cleared.
    // Since BLUM-8748 the cascade is one transaction and claims are only cleared once it has committed, so both
    // clears see the organisation already suspended.
    [Fact]
    public async Task ShouldBlockAUserReactivatedMidCascade_OnceTheOrganisationIsSuspended()
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var claimsCache = new ReactivatingClaimsCache(services.GetRequiredService<IUserClaimsCache>(), _scopeFactory, _lead);
        var mfaSessionService = services.GetRequiredService<IMfaSessionService>();
        var auditLogRepository = services.GetRequiredService<IAuditLogRepository>();
        var transactionRunner = services.GetRequiredService<ITransactionRunner>();
        var handler = new SuspendLocalAuthorityCommandHandler(
            services.GetRequiredService<IOrganisationRepository>(),
            services.GetRequiredService<IOrganisationUserRepository>(),
            new UserDeactivator(
                services.GetRequiredService<IUserRepository>(),
                auditLogRepository,
                mfaSessionService,
                claimsCache,
                services.GetRequiredService<IDateTimeProvider>(),
                transactionRunner),
            mfaSessionService,
            claimsCache,
            auditLogRepository,
            transactionRunner,
            services.GetRequiredService<PermissionsService>(),
            services.GetRequiredService<IDateTimeProvider>());

        var result = await handler.Handle(new SuspendLocalAuthorityCommand(_orgWithUser.Id.Value), CancellationToken.None);

        result.IsRight.Should().BeTrue();
        (await GetUserAsync(_orgUser)).Status.Should().Be(UserStatus.Deactivated);
        (await GetUserAsync(_lead)).Status.Should().Be(UserStatus.Active, "the stand-in reactivated them mid-cascade");

        claimsCache.SessionOpenedMidCascade.Should().NotBeNull();
        (await GetSessionAsync(claimsCache.SessionOpenedMidCascade!)).IsRevoked.Should().BeTrue();
        claimsCache.OrganisationStatusWhenLeadWasCleared.Should().Equal(OnboardingStatuses.Suspended, OnboardingStatuses.Suspended);
    }

    [Fact]
    public async Task ShouldNotBlockAdmin_FromViewingSuspendedLA()
    {
        var suspend = await Client.PostAsync($"organisations/local-authorities/{_active.Id.Value}/suspend", null);
        suspend.StatusCode.Should().Be(HttpStatusCode.OK);

        // The DHSC admin has no organisation, so is never blocked and retains read access (AC #3).
        var view = await Client.GetAsync($"organisations/local-authorities/{_active.Id.Value}");
        view.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await view.ToResponseModel<LocalAuthorityDto>();
        dto.OnboardingStatus.Should().Be(OnboardingStatuses.Suspended);
    }

    [Fact]
    public async Task ShouldForbidNonAdmin_FromSuspending()
    {
        AuthenticateAs(Role.OrganisationAdmin, _orgUser.UserId.Value, _orgWithUser.Id.Value);

        var response = await Client.PostAsync($"organisations/local-authorities/{_active.Id.Value}/suspend", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    public async Task InitializeAsync()
    {
        _orgUser = OrganisationUsersData.MainOrganisationUser(_orgWithUser.Id);
        _lead = OrganisationUsersData.Member(_orgWithUser.Id, UserRole.Lead, "lead@council-with-user.gov.uk");
        _alreadyDeactivated = OrganisationUsersData.Member(
            _orgWithUser.Id, UserRole.Lead, "leaver@council-with-user.gov.uk", UserStatus.Deactivated);
        _neighbourUser = OrganisationUsersData.Member(_neighbour.Id, UserRole.OrganisationAdmin, "admin@neighbouring-council.gov.uk");
        _vcseUser = OrganisationUsersData.Member(_vcse.Id, UserRole.OrganisationAdmin, "admin@vcse-partner.org.uk");

        await Context.Organisations.AddRangeAsync(_active, _invited, _suspended, _orgWithUser, _neighbour, _vcse);
        await Context.OrganisationUsers.AddRangeAsync(_orgUser, _lead, _alreadyDeactivated, _neighbourUser, _vcseUser);
        await SaveChangesAsync();
    }

    private async Task<User> GetUserAsync(OrganisationUser member)
    {
        return await Context.Users.AsNoTracking().FirstAsync(x => x.Id == member.UserId);
    }

    private async Task<MfaSessionId> SeedSessionAsync(UserId userId)
    {
        var session = MfaSession.New(
            MfaSessionId.New(),
            userId,
            ipAddressHash: "seeded-ip-hash",
            userAgentHash: "seeded-user-agent-hash",
            ipAddress: "203.0.113.10",
            deviceLabel: "Chrome on Windows",
            utcNow: DateTime.UtcNow);

        await Context.MfaSessions.AddAsync(session);
        await SaveChangesAsync();

        return session.Id;
    }

    private async Task<MfaSession> GetSessionAsync(MfaSessionId sessionId)
    {
        return await Context.MfaSessions.AsNoTracking().FirstAsync(x => x.Id == sessionId);
    }

    // Signs in as the member through the real claims transformation and calls an endpoint every LA role may use.
    private async Task<HttpResponseMessage> GetUsersAsMemberAsync(OrganisationUser member)
    {
        AuthenticateThroughClaimsTransformation(member.User!.SubId.Value);
        return await Client.GetAsync($"organisations/{member.OrganisationId.Value}/users");
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("error").GetString();
    }

    public async Task DisposeAsync()
    {
        Context.AuditLogs.RemoveRange(Context.AuditLogs);
        Context.MfaSessions.RemoveRange(Context.MfaSessions);
        Context.OrgFundingAllocations.RemoveRange(Context.OrgFundingAllocations);
        Context.FundingWindows.RemoveRange(Context.FundingWindows);
        Context.OrganisationUsers.RemoveRange(Context.OrganisationUsers);
        Context.Users.RemoveRange(Context.Users);
        Context.Organisations.RemoveRange(Context.Organisations);
        await SaveChangesAsync();
        AuthenticateAsAdmin();
    }

    // Stands in for an LA admin reactivating the Lead, and the Lead signing back in, at the first moment the
    // cascade has finished with them: when it clears their cached claims. Uses its own scope, as a concurrent
    // request would. Also records the organisation's status each time the Lead's claims are cleared.
    private sealed class ReactivatingClaimsCache(IUserClaimsCache inner, IServiceScopeFactory scopeFactory, OrganisationUser lead)
        : IUserClaimsCache
    {
        public List<string> OrganisationStatusWhenLeadWasCleared { get; } = [];

        public MfaSessionId? SessionOpenedMidCascade { get; private set; }

        public void Invalidate(SubId subId)
        {
            inner.Invalidate(subId);

            if (subId != lead.User!.SubId)
            {
                return;
            }

            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var organisation = db.Organisations.AsNoTracking().First(x => x.Id == lead.OrganisationId);
            OrganisationStatusWhenLeadWasCleared.Add(organisation.OnboardingStatus.ToString());

            if (SessionOpenedMidCascade is not null)
            {
                return;
            }

            db.Users.First(x => x.Id == lead.UserId).Reactivate(DateTime.UtcNow);
            var session = MfaSession.New(
                MfaSessionId.New(),
                lead.UserId,
                ipAddressHash: "mid-cascade-ip-hash",
                userAgentHash: "mid-cascade-user-agent-hash",
                ipAddress: "203.0.113.20",
                deviceLabel: "Firefox on macOS",
                utcNow: DateTime.UtcNow);
            db.MfaSessions.Add(session);
            db.SaveChanges();

            SessionOpenedMidCascade = session.Id;
        }
    }
}