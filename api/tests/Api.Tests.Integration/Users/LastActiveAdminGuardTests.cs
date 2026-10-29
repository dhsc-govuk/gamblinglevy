using System.Net;
using System.Net.Http.Json;
using Api.Dtos;
using Domain.AuditLogs;
using Domain.Locations;
using Domain.LocationUsers;
using Domain.Organisations;
using Domain.OrganisationUsers;
using Domain.Users;
using Domain.ValueObjects;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Tests.Common;
using Tests.Data;
using Xunit;

namespace Api.Tests.Integration.Users;

/// <summary>
/// BLUM-8751 — the user-lifecycle commands are now one per action, sent by both the DHSC routes (admin/users/…)
/// and the LA routes (organisations/{id}/users/…), with one LastActiveAdminGuard. Before that each route carried
/// its own copy and they disagreed: DHSC could deactivate or delete an LA's last LA Admin on its own routes but
/// not on the LA route, the LA edit form could deactivate one outright (and without revoking their sessions),
/// DHSC could demote the last DHSC admin, and an LA delete never cleared location_users. These pin the shared
/// behaviour on every route.
/// </summary>
public class LastActiveAdminGuardTests(IntegrationTestWebFactory factory)
    : BaseIntegrationTest(factory), IAsyncLifetime
{
    private readonly Organisation _organisation = OrganisationsData.MainOrganisation;
    private readonly Organisation _otherOrganisation = OrganisationsData.SecondOrganisation;

    public static TheoryData<string, string> Routes() => new()
    {
        { "admin", "deactivate" },
        { "admin", "delete" },
        { "la", "deactivate" },
        { "la", "delete" },
    };

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task ShouldRefuseToRemoveTheLastActiveLaAdmin_OnEveryRoute(string surface, string action)
    {
        // Arrange — the organisation's one LA Admin, removed by DHSC.
        var laAdmin = await SeedMemberAsync(UserRole.OrganisationAdmin, "only.admin@council.gov.uk");

        // Act
        var response = await SendAsync(surface, action, laAdmin.UserId);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var user = await Context.Users.AsNoTracking().FirstAsync(x => x.Id == laAdmin.UserId);
        user.Status.Should().Be(UserStatus.Active);
        (await Context.OrganisationUsers.AsNoTracking().AnyAsync(x => x.UserId == laAdmin.UserId)).Should().BeTrue();
        (await RemovalAuditRowsAsync()).Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task ShouldRemoveAnLaAdmin_WhenAnotherActiveLaAdminRemains(string surface, string action)
    {
        // Arrange
        var laAdmin = await SeedMemberAsync(UserRole.OrganisationAdmin, "first.admin@council.gov.uk");
        await SeedMemberAsync(UserRole.OrganisationAdmin, "second.admin@council.gov.uk");

        // Act
        var response = await SendAsync(surface, action, laAdmin.UserId);

        // Assert
        response.IsSuccessStatusCode.Should().BeTrue();
        (await RemovalAuditRowsAsync()).Should().ContainSingle()
            .Which.OrganisationId.Should().Be(_organisation.Id);
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task ShouldRefuseToRemoveTheLastActiveSuperAdmin_OnEveryRoute(string surface, string action)
    {
        // Arrange — a DHSC caller who is not in the database, so the seeded MainAdmin is the only active admin and
        // the self-refusal cannot answer first.
        AuthenticateAs(Role.Admin, Guid.NewGuid(), _organisation.Id.Value);

        // Act
        var response = await SendAsync(surface, action, AdminsData.MainAdmin.Id);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var admin = await Context.Users.AsNoTracking().FirstAsync(x => x.Id == AdminsData.MainAdmin.Id);
        admin.Status.Should().Be(UserStatus.Active);
        (await RemovalAuditRowsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task ShouldRefuseToDemoteTheLastActiveLaAdmin_OnTheLaEditForm()
    {
        // Arrange
        var laAdmin = await SeedMemberAsync(UserRole.OrganisationAdmin, "only.admin@council.gov.uk");

        // Act
        var response = await Client.PutAsJsonAsync(LaEditRoute, LaEditPayload(laAdmin, Status.Active, Role.Lead));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await Context.Users.AsNoTracking().FirstAsync(x => x.Id == laAdmin.UserId)).Role.Should().Be(UserRole.OrganisationAdmin);
    }

    [Fact]
    public async Task ShouldRefuseToDeactivateTheLastActiveLaAdmin_OnTheLaEditForm()
    {
        // Arrange
        var laAdmin = await SeedMemberAsync(UserRole.OrganisationAdmin, "only.admin@council.gov.uk");

        // Act — the role is kept, only the status changes, which the old demotion-only guard let through.
        var response = await Client.PutAsJsonAsync(LaEditRoute, LaEditPayload(laAdmin, Status.Deactivated, Role.OrganisationAdmin));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await Context.Users.AsNoTracking().FirstAsync(x => x.Id == laAdmin.UserId)).Status.Should().Be(UserStatus.Active);
        (await RemovalAuditRowsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task ShouldRefuseToDeactivateThemselves_OnTheLaEditForm()
    {
        // Arrange — not the last LA Admin, so only the self-refusal stands between them and their own deactivation,
        // which would revoke the very session making the request.
        var laAdmin = await SeedMemberAsync(UserRole.OrganisationAdmin, "admin@council.gov.uk");
        await SeedMemberAsync(UserRole.OrganisationAdmin, "other.admin@council.gov.uk");
        var session = MfaData.Session(laAdmin.UserId);
        await Context.MfaSessions.AddAsync(session);
        await SaveChangesAsync();

        AuthenticateAs(Role.OrganisationAdmin, laAdmin.UserId.Value, _organisation.Id.Value);

        // Act
        var response = await Client.PutAsJsonAsync(LaEditRoute, LaEditPayload(laAdmin, Status.Deactivated, Role.OrganisationAdmin));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await Context.Users.AsNoTracking().FirstAsync(x => x.Id == laAdmin.UserId)).Status.Should().Be(UserStatus.Active);
        (await Context.MfaSessions.AsNoTracking().FirstAsync(x => x.Id == session.Id)).IsRevoked.Should().BeFalse();
        (await Context.AuditLogs.AsNoTracking().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task ShouldDeactivateALead_OnTheLaEditForm_ThroughTheFullDeactivationSequence()
    {
        // Arrange — an LA Admin deactivating a signed-in Lead through the edit form.
        var laAdmin = await SeedMemberAsync(UserRole.OrganisationAdmin, "admin@council.gov.uk");
        var lead = await SeedMemberAsync(UserRole.Lead, "lead@council.gov.uk");
        var session = MfaData.Session(lead.UserId);
        await Context.MfaSessions.AddAsync(session);
        await SaveChangesAsync();

        AuthenticateAs(Role.OrganisationAdmin, laAdmin.UserId.Value, _organisation.Id.Value);

        // Act
        var response = await Client.PutAsJsonAsync(LaEditRoute, LaEditPayload(lead, Status.Deactivated, Role.Lead));

        // Assert — deactivated, its session revoked, and a user.deactivated row in the LA's own audit log
        // alongside the edit's user.updated row. Flipping the status directly did neither of the first two.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.ToResponseModel<UserDto>()).Status.Should().Be(Status.Deactivated);
        (await Context.Users.AsNoTracking().FirstAsync(x => x.Id == lead.UserId)).Status.Should().Be(UserStatus.Deactivated);
        (await Context.MfaSessions.AsNoTracking().FirstAsync(x => x.Id == session.Id)).IsRevoked.Should().BeTrue();

        var rows = await Context.AuditLogs.AsNoTracking().Where(x => x.TargetId == lead.UserId.Value).ToListAsync();
        rows.Select(x => x.Action).Should().BeEquivalentTo([AuditAction.UserUpdated, AuditAction.UserDeactivated]);
        rows.Should().OnlyContain(x => x.OrganisationId == _organisation.Id && x.ActorUserId == laAdmin.UserId);
    }

    [Fact]
    public async Task ShouldRefuseToDemoteTheLastActiveSuperAdmin_EvenThemselves_OnTheDhscEditForm()
    {
        // Act — the default caller is MainAdmin, the only active DHSC admin, demoting themselves.
        var response = await Client.PutAsJsonAsync(
            "admin/users/edit",
            DhscEditPayload(AdminsData.MainAdmin, Role.Lead, _organisation.Id.Value));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await Context.Users.AsNoTracking().FirstAsync(x => x.Id == AdminsData.MainAdmin.Id)).Role.Should().Be(UserRole.Admin);
        (await Context.OrganisationUsers.AsNoTracking().AnyAsync(x => x.UserId == AdminsData.MainAdmin.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task ShouldAllowDemotingASuperAdmin_WhenAnotherActiveSuperAdminRemains()
    {
        // Arrange
        await Context.Users.AddAsync(AdminsData.SecondAdmin);
        await SaveChangesAsync();

        // Act
        var response = await Client.PutAsJsonAsync(
            "admin/users/edit",
            DhscEditPayload(AdminsData.SecondAdmin, Role.Lead, _organisation.Id.Value));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Context.Users.AsNoTracking().FirstAsync(x => x.Id == AdminsData.SecondAdmin.Id)).Role.Should().Be(UserRole.Lead);
    }

    [Fact]
    public async Task ShouldRefuseToMoveTheLastActiveLaAdmin_ToAnotherOrganisation()
    {
        // Arrange
        var laAdmin = await SeedMemberAsync(UserRole.OrganisationAdmin, "only.admin@council.gov.uk");

        // Act — still an LA Admin, but of a different LA, which strands this one just as a demotion would.
        var response = await Client.PutAsJsonAsync(
            "admin/users/edit",
            DhscEditPayload(laAdmin.User!, Role.OrganisationAdmin, _otherOrganisation.Id.Value));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var membership = await Context.OrganisationUsers.AsNoTracking().FirstAsync(x => x.UserId == laAdmin.UserId);
        membership.OrganisationId.Should().Be(_organisation.Id);
    }

    [Fact]
    public async Task ShouldDeleteALeadWithALocationRow_ThroughTheLaRoute()
    {
        // Arrange — location_users holds a Restrict foreign key to users. The LA route's own copy of the delete
        // never removed it, so this failed with a 500 after the Entra account had already been deleted.
        var laAdmin = await SeedMemberAsync(UserRole.OrganisationAdmin, "admin@council.gov.uk");
        var lead = await SeedMemberAsync(UserRole.Lead, "lead@council.gov.uk");
        var location = LocationsData.MainLocation(_organisation.Id);
        await Context.Locations.AddAsync(location);
        await Context.LocationUsers.AddAsync(LocationUser.New(LocationUserId.New(), lead.UserId, location.Id));
        await SaveChangesAsync();

        AuthenticateAs(Role.OrganisationAdmin, laAdmin.UserId.Value, _organisation.Id.Value);

        // Act
        var response = await Client.DeleteAsync($"organisations/{_organisation.Id.Value}/users/{lead.UserId.Value}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Context.Users.AsNoTracking().AnyAsync(x => x.Id == lead.UserId)).Should().BeFalse();
        (await Context.OrganisationUsers.AsNoTracking().AnyAsync(x => x.UserId == lead.UserId)).Should().BeFalse();
        (await Context.LocationUsers.AsNoTracking().AnyAsync(x => x.UserId == lead.UserId)).Should().BeFalse();
        (await RemovalAuditRowsAsync()).Should().ContainSingle()
            .Which.OrganisationId.Should().Be(_organisation.Id);
    }

    [Theory]
    [InlineData("deactivate", AuditAction.UserDeactivated, Status.Active)]
    [InlineData("reactivate", AuditAction.UserReactivated, Status.Deactivated)]
    [InlineData("reset-mfa", AuditAction.UserMfaReset, Status.Active)]
    public async Task ShouldScopeTheAuditRow_ToTheTargetsOrganisation_OnTheDhscRoute(string action, string auditAction, string status)
    {
        // Arrange — the DHSC route used to write these rows with no organisation, so a DHSC action on an LA's
        // user never reached that LA's own audit log, while the same action through the LA route did.
        var lead = await SeedMemberAsync(UserRole.Lead, "lead@council.gov.uk", UserStatus.From(status));

        // Act
        var response = await Client.PostAsync($"admin/users/{lead.UserId.Value}/{action}", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var row = await Context.AuditLogs.AsNoTracking().SingleAsync(x => x.Action == auditAction);
        row.OrganisationId.Should().Be(_organisation.Id);
    }

    [Theory]
    [InlineData("deactivate")]
    [InlineData("reset-mfa")]
    public async Task ShouldNotFindADhscUser_ForAnLaAdmin_OnTheLaRoute(string action)
    {
        // Arrange
        var laAdmin = await SeedMemberAsync(UserRole.OrganisationAdmin, "admin@council.gov.uk");
        AuthenticateAs(Role.OrganisationAdmin, laAdmin.UserId.Value, _organisation.Id.Value);

        // Act
        var response = await Client.PostAsync(
            $"organisations/{_organisation.Id.Value}/users/{AdminsData.MainAdmin.Id.Value}/{action}",
            null);

        // Assert — outside the caller's scope reads as "does not exist", never "forbidden".
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Context.Users.AsNoTracking().FirstAsync(x => x.Id == AdminsData.MainAdmin.Id)).Status.Should().Be(UserStatus.Active);
        (await Context.AuditLogs.AsNoTracking().AnyAsync()).Should().BeFalse();
    }

    public async Task InitializeAsync()
    {
        await ClearAsync();

        await Context.Users.AddAsync(AdminsData.MainAdmin);
        await Context.Organisations.AddRangeAsync(_organisation, _otherOrganisation);
        await SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await ClearAsync();
        AuthenticateAsAdmin();
    }

    private string LaEditRoute => $"organisations/{_organisation.Id.Value}/users/edit";

    private async Task ClearAsync()
    {
        // Join rows first: organisation_users and location_users hold Restrict foreign keys to users.
        Context.AuditLogs.RemoveRange(Context.AuditLogs);
        Context.MfaSessions.RemoveRange(Context.MfaSessions);
        Context.UserMfas.RemoveRange(Context.UserMfas);
        Context.OrganisationUsers.RemoveRange(Context.OrganisationUsers);
        Context.LocationUsers.RemoveRange(Context.LocationUsers);
        await SaveChangesAsync();

        Context.Users.RemoveRange(Context.Users);
        Context.Locations.RemoveRange(Context.Locations);
        Context.Organisations.RemoveRange(Context.Organisations);
        await SaveChangesAsync();
    }

    private async Task<OrganisationUser> SeedMemberAsync(UserRole role, string email, UserStatus? status = null)
    {
        var member = OrganisationUsersData.Member(_organisation.Id, role, email, status);
        await Context.OrganisationUsers.AddAsync(member);
        await SaveChangesAsync();

        return member;
    }

    private Task<HttpResponseMessage> SendAsync(string surface, string action, UserId userId)
    {
        var route = surface == "admin"
            ? $"admin/users/{userId.Value}"
            : $"organisations/{_organisation.Id.Value}/users/{userId.Value}";

        return action == "delete"
            ? Client.DeleteAsync(route)
            : Client.PostAsync($"{route}/{action}", null);
    }

    private Task<List<AuditLog>> RemovalAuditRowsAsync() =>
        Context.AuditLogs.AsNoTracking()
            .Where(x => x.Action == AuditAction.UserDeactivated || x.Action == AuditAction.UserDeleted)
            .ToListAsync();

    private static UserDto LaEditPayload(OrganisationUser member, string status, string role) =>
        new(
            Id: member.UserId.Value,
            FirstName: member.User!.Name.FirstName,
            LastName: member.User.Name.LastName,
            Email: member.User.Email,
            Status: status,
            Role: role,
            OrganisationName: null);

    private static UpdateUserPayload DhscEditPayload(User user, string role, Guid? organisationId) =>
        new(
            Id: user.Id.Value,
            FirstName: user.Name.FirstName,
            LastName: user.Name.LastName,
            Email: user.Email,
            Role: role,
            OrganisationId: organisationId);
}