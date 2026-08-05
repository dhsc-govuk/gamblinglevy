using System.Net;
using System.Text.Json;
using Api.Dtos;
using Domain.AuditLogs;
using Domain.Organisations;
using Domain.OrganisationUsers;
using Domain.ValueObjects;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
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
        OnboardingStatus.Active);

    private readonly Organisation _invited = Organisation.New(
        OrganisationId.New(),
        "Invited Council",
        "E06000061",
        Region.London,
        OrgType.LocalAuthority,
        "E1000001",
        OnboardingStatus.Invited);

    private readonly Organisation _suspended = Organisation.New(
        OrganisationId.New(),
        "Suspended Council",
        "E06000062",
        Region.London,
        OrgType.LocalAuthority,
        "E1000001",
        OnboardingStatus.Suspended);

    private readonly Organisation _orgWithUser = Organisation.New(
        OrganisationId.New(),
        "Council With User",
        "E06000063",
        Region.London,
        OrgType.LocalAuthority,
        "E1000001",
        OnboardingStatus.Active);

    private OrganisationUser _orgUser = null!;

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
    public async Task ShouldBlockOrganisationUser_AfterSuspension()
    {
        // Before suspension the org's user can reach an org-scoped endpoint (not blocked).
        AuthenticateAs(Role.OrganisationAdmin, _orgUser.UserId.Value, _orgWithUser.Id.Value);
        var before = await Client.GetAsync("funding/dashboard");
        before.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);

        // DHSC admin suspends the organisation.
        AuthenticateAsAdmin();
        var suspend = await Client.PostAsync($"organisations/local-authorities/{_orgWithUser.Id.Value}/suspend", null);
        suspend.StatusCode.Should().Be(HttpStatusCode.OK);

        // The org's user is now blocked with the explicit ORGANISATION_SUSPENDED envelope.
        AuthenticateAs(Role.OrganisationAdmin, _orgUser.UserId.Value, _orgWithUser.Id.Value);
        var after = await Client.GetAsync("funding/dashboard");

        after.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await after.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("error").GetString().Should().Be("ORGANISATION_SUSPENDED");
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

        await Context.Organisations.AddRangeAsync(_active, _invited, _suspended, _orgWithUser);
        await Context.OrganisationUsers.AddAsync(_orgUser);
        await SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        Context.AuditLogs.RemoveRange(Context.AuditLogs);
        Context.OrgFundingAllocations.RemoveRange(Context.OrgFundingAllocations);
        Context.FundingWindows.RemoveRange(Context.FundingWindows);
        Context.OrganisationUsers.RemoveRange(Context.OrganisationUsers);
        Context.Users.RemoveRange(Context.Users);
        Context.Organisations.RemoveRange(Context.Organisations);
        await SaveChangesAsync();
        AuthenticateAsAdmin();
    }
}