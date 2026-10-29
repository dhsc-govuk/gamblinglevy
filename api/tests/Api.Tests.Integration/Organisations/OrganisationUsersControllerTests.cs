using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Dtos;
using Application.AuditLogs;
using Application.AuditLogs.Dtos;
using Domain.AuditLogs;
using Domain.Organisations;
using Domain.OrganisationUsers;
using Domain.Users;
using Domain.ValueObjects;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tests.Common;
using Tests.Common.Services;
using Tests.Data;
using Xunit;

namespace Api.Tests.Integration.Organisations;

public class OrganisationUsersControllerTests : BaseIntegrationTest, IAsyncLifetime
{
    private readonly Organisation _mainOrganisation = OrganisationsData.MainOrganisation;
    private readonly Organisation _otherOrganisation = OrganisationsData.SecondOrganisation;

    private readonly OrganisationUser _mainOrganisationAdmin;
    private readonly OrganisationUser _secondOrganisationAdmin;

    private readonly string _mainCreateOrganisationUserRoute;
    private readonly string _mainUpdateOrganisationUserRoute;

    private readonly InMemoryTemporaryPasswordEmailService _temporaryPasswordEmails;

    public OrganisationUsersControllerTests(IntegrationTestWebFactory factory) : base(factory)
    {
        _temporaryPasswordEmails = factory.Services.GetRequiredService<InMemoryTemporaryPasswordEmailService>();

        _mainOrganisationAdmin = OrganisationUsersData.MainOrganisationUser(_mainOrganisation.Id);
        _secondOrganisationAdmin = OrganisationUsersData.SecondOrganisationUser(_mainOrganisation.Id);

        _mainCreateOrganisationUserRoute = $"organisations/{_mainOrganisation.Id}/users/create";
        _mainUpdateOrganisationUserRoute = $"organisations/{_mainOrganisation.Id}/users/edit";
    }

    [Fact]
    public async Task ShouldCreateOrganisationAdmin()
    {
        // Arrange
        var request = new UserDto(
            Id: null,
            FirstName: _mainOrganisationAdmin.User!.Name.FirstName,
            LastName: _mainOrganisationAdmin.User.Name.LastName,
            Email: _mainOrganisationAdmin.User.Email,
            Status: _mainOrganisationAdmin.User.Status.ToString(),
            Role: _mainOrganisationAdmin.User.Role.ToString(),
            OrganisationName: null);

        // Act
        var response = await Client.PostAsJsonAsync(_mainCreateOrganisationUserRoute, request);

        // Assert
        response.IsSuccessStatusCode.Should().BeTrue();
        var organisationAdmin = await response.ToResponseModel<UserDto>();
        var dbOrganisationAdmin = await Context.OrganisationUsers
            .AsNoTracking()
            .Include(x => x.User)
            .FirstAsync(x => x.Id == new OrganisationUserId(organisationAdmin.Id!.Value));
        dbOrganisationAdmin.User!.Name.FirstName.Should().Be(request.FirstName);
        dbOrganisationAdmin.User.Name.LastName.Should().Be(request.LastName);
        dbOrganisationAdmin.User.Email.Should().Be(request.Email);
        dbOrganisationAdmin.User.Status.ToString().Should().Be(request.Status.ToString());
        dbOrganisationAdmin.User.Role.Should().Be(UserRole.OrganisationAdmin);
    }

    [Fact]
    public async Task ShouldReportFailure_AndCreateNothing_WhenTheDirectoryLookupFails()
    {
        // BLUM-8746 — a lookup Graph refused used to escape the handler as an unhandled exception. It is now
        // returned, so the request is answered through UserErrorFactory, and the handler must not go on to
        // create an account for an address it could not look up.
        var request = new UserDto(
            Id: null,
            FirstName: _mainOrganisationAdmin.User!.Name.FirstName,
            LastName: _mainOrganisationAdmin.User.Name.LastName,
            Email: _mainOrganisationAdmin.User.Email,
            Status: _mainOrganisationAdmin.User.Status.ToString(),
            Role: _mainOrganisationAdmin.User.Role.ToString(),
            OrganisationName: null);
        TestGraphContext.FailFindByEmail();

        try
        {
            var response = await Client.PostAsJsonAsync(_mainCreateOrganisationUserRoute, request);

            response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
            (await response.Content.ReadAsStringAsync()).Should().Contain("Unknown error for user");
            (await Context.Users.AsNoTracking().AnyAsync(x => x.Email == request.Email)).Should().BeFalse();
        }
        finally
        {
            TestGraphContext.Reset();
        }
    }

    [Fact]
    public async Task ShouldUpdateOrganisationAdmin()
    {
        // Arrange
        var request = new UserDto(
            Id: _secondOrganisationAdmin.Id.Value,
            FirstName: _mainOrganisationAdmin.User!.Name.FirstName,
            LastName: _mainOrganisationAdmin.User.Name.LastName,
            Email: _mainOrganisationAdmin.User.Email,
            Status: _mainOrganisationAdmin.User.Status.ToString(),
            Role: _mainOrganisationAdmin.User.Role.ToString(),
            OrganisationName: null);

        // Act
        var response = await Client.PutAsJsonAsync(_mainUpdateOrganisationUserRoute, request);

        // Assert
        response.IsSuccessStatusCode.Should().BeTrue();
        var organisationAdmin = await response.ToResponseModel<UserDto>();
        var dbOrganisationAdmin = await Context.OrganisationUsers
            .AsNoTracking()
            .Include(x => x.User)
            .FirstAsync(x => x.UserId == new UserId(organisationAdmin.Id!.Value));
        dbOrganisationAdmin.User!.Name.FirstName.Should().Be(request.FirstName);
        dbOrganisationAdmin.User.Name.LastName.Should().Be(request.LastName);
        dbOrganisationAdmin.User.Email.Should().Be(request.Email);
        dbOrganisationAdmin.User.Status.ToString().Should().Be(request.Status.ToString());
        dbOrganisationAdmin.User.Role.Should().Be(UserRole.OrganisationAdmin);
    }

    [Fact]
    public async Task ShouldDeactivateOrganisationUser_AndAuditItWithoutACause()
    {
        // Arrange — a Lead, so the last-LA-admin guard does not apply.
        var lead = OrganisationUsersData.Member(_mainOrganisation.Id, UserRole.Lead, "lead@main-organisation.gov.uk");
        await Context.OrganisationUsers.AddAsync(lead);
        await SaveChangesAsync();

        // Act
        var response = await Client.PostAsync($"organisations/{_mainOrganisation.Id.Value}/users/{lead.UserId.Value}/deactivate", null);

        // Assert
        response.IsSuccessStatusCode.Should().BeTrue();
        var saved = await Context.Users.AsNoTracking().FirstAsync(x => x.Id == lead.UserId);
        saved.Status.Should().Be(UserStatus.Deactivated);

        var audit = await Context.AuditLogs.AsNoTracking()
            .Where(x => x.TargetType == AuditTargetType.User && x.TargetId == lead.UserId.Value)
            .ToListAsync();
        var row = audit.Should().ContainSingle(x => x.Action == AuditAction.UserDeactivated).Subject;
        row.OrganisationId.Should().Be(_mainOrganisation.Id);

        // Only a deactivation that happened because of something else, such as its organisation being
        // deactivated, names a cause (BLUM-7667). One requested by name carries the plain snapshot.
        using var after = JsonDocument.Parse(row.After!);
        after.RootElement.GetProperty("status").GetString().Should().Be(Status.Deactivated);
        after.RootElement.TryGetProperty("cause", out _).Should().BeFalse();
    }

    // BLUM-8161 review: a failed temporary-password email must not report success, mirroring the same fix on
    // the DHSC-side commands — see AdminUsersControllerTests for the full rationale.
    [Fact]
    public async Task ShouldEnablePasswordLogin_SendEmail_AndWriteAudit()
    {
        var response = await Client.PostAsync(
            $"organisations/{_mainOrganisation.Id.Value}/users/{_secondOrganisationAdmin.UserId.Value}/enable-password-login", null);

        response.IsSuccessStatusCode.Should().BeTrue();

        var saved = await Context.Users.AsNoTracking().FirstAsync(x => x.Id == _secondOrganisationAdmin.UserId);
        saved.PasswordLoginEnabled.Should().BeTrue();

        _temporaryPasswordEmails.Sent.Should().Contain(s => s.Email == _secondOrganisationAdmin.User!.Email);

        // Scoped to the user's organisation, or the row never reaches that LA's own audit log (BLUM-8761).
        var audit = await Context.AuditLogs.AsNoTracking()
            .Where(x => x.TargetType == AuditTargetType.User && x.TargetId == _secondOrganisationAdmin.UserId.Value && x.Action == AuditAction.UserPasswordLoginEnabled)
            .ToListAsync();
        audit.Should().ContainSingle().Which.OrganisationId.Should().Be(_mainOrganisation.Id);
    }

    [Fact]
    public async Task ShouldResetMfa_AsOrganisationAdmin_AndAuditItAgainstTheOrganisation()
    {
        // Arrange — a Lead who has enrolled and is signed in on two devices, and another LA to look from.
        var lead = OrganisationUsersData.Member(_mainOrganisation.Id, UserRole.Lead, "mfa.lead@main-organisation.gov.uk");
        var otherAdmin = OtherOrganisationAdmin();
        await Context.Organisations.AddAsync(_otherOrganisation);
        await Context.OrganisationUsers.AddRangeAsync(lead, otherAdmin);
        await Context.UserMfas.AddAsync(MfaData.Enabled(lead.UserId));
        await Context.MfaSessions.AddRangeAsync(MfaData.Session(lead.UserId), MfaData.Session(lead.UserId));
        await SaveChangesAsync();

        AuthenticateAs(Role.OrganisationAdmin, _secondOrganisationAdmin.UserId.Value, _mainOrganisation.Id.Value);

        // Act
        var response = await Client.PostAsync(UserRoute(_mainOrganisation, lead, "reset-mfa"), null);

        // Assert — the enrolment is gone, so the next sign-in sets up again, and every session is ended.
        // Revoked sessions stay as rows: the session history is not rewritten.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.ToResponseModel<UserDto>()).Id.Should().Be(lead.UserId.Value);

        (await Context.UserMfas.AsNoTracking().AnyAsync(x => x.UserId == lead.UserId)).Should().BeFalse();
        var sessions = await Context.MfaSessions.AsNoTracking().Where(x => x.UserId == lead.UserId).ToListAsync();
        sessions.Should().HaveCount(2).And.OnlyContain(x => x.IsRevoked);

        var row = (await AuditRowsAsync(AuditAction.UserMfaReset)).Should().ContainSingle().Subject;
        row.ActorUserId.Should().Be(_secondOrganisationAdmin.UserId);
        row.TargetType.Should().Be(AuditTargetType.User);
        row.TargetId.Should().Be(lead.UserId.Value);
        row.OrganisationId.Should().Be(_mainOrganisation.Id);
        row.After.Should().BeNull();

        using var before = JsonDocument.Parse(row.Before!);
        before.RootElement.GetProperty("email").GetString().Should().Be(lead.User!.Email);
        before.RootElement.GetProperty("role").GetString().Should().Be(Role.Lead);

        // The row is the LA's own: its audit log shows it, and no other LA's does.
        var own = await AuditLogAsync(AuditAction.UserMfaReset);
        own.Items.Should().ContainSingle().Which.Id.Should().Be(row.Id.Value);

        AuthenticateAs(Role.OrganisationAdmin, otherAdmin.UserId.Value, _otherOrganisation.Id.Value);
        var other = await AuditLogAsync(AuditAction.UserMfaReset);
        other.Items.Should().BeEmpty();
        other.TotalCount.Should().Be(0);
        (await Client.GetAsync($"organisation-admin/audit-logs/{row.Id.Value}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ShouldNotResetMfa_ForAnotherOrganisationsUser(bool routeNamesTheTargetsOrganisation)
    {
        // Arrange
        var otherAdmin = OtherOrganisationAdmin();
        var session = MfaData.Session(otherAdmin.UserId);
        await Context.Organisations.AddAsync(_otherOrganisation);
        await Context.OrganisationUsers.AddAsync(otherAdmin);
        await Context.UserMfas.AddAsync(MfaData.Enabled(otherAdmin.UserId));
        await Context.MfaSessions.AddAsync(session);
        await SaveChangesAsync();

        AuthenticateAs(Role.OrganisationAdmin, _secondOrganisationAdmin.UserId.Value, _mainOrganisation.Id.Value);

        // Act — the organisation in the route is not what scopes the lookup, so neither naming the caller's
        // own organisation nor the target's gets an LA admin to another LA's user.
        var routeOrganisation = routeNamesTheTargetsOrganisation ? _otherOrganisation : _mainOrganisation;
        var response = await Client.PostAsync(UserRoute(routeOrganisation, otherAdmin, "reset-mfa"), null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var mfa = await Context.UserMfas.AsNoTracking().FirstAsync(x => x.UserId == otherAdmin.UserId);
        mfa.IsEnabled.Should().BeTrue();
        (await Context.MfaSessions.AsNoTracking().FirstAsync(x => x.Id == session.Id)).IsRevoked.Should().BeFalse();
        (await AuditRowsAsync(AuditAction.UserMfaReset)).Should().BeEmpty();
    }

    [Theory]
    [InlineData("reset-mfa", AuditAction.UserMfaReset)]
    [InlineData("enable-password-login", AuditAction.UserPasswordLoginEnabled)]
    public async Task ShouldScopeTheAuditRow_ToTheTargetsOrganisation_NotTheRouteOrganisation(string action, string auditAction)
    {
        // Arrange — DHSC is not scoped to an organisation, and nothing compares the organisation in the route
        // with the user's, so a DHSC caller can reach another LA's user through this LA's route. The row has
        // to follow the user: taking the organisation from the route would write one LA's user snapshot into
        // a different LA's audit log.
        var otherAdmin = OtherOrganisationAdmin();
        await Context.Organisations.AddAsync(_otherOrganisation);
        await Context.OrganisationUsers.AddAsync(otherAdmin);
        await SaveChangesAsync();

        AuthenticateAsAdmin();

        // Act
        var response = await Client.PostAsync(UserRoute(_mainOrganisation, otherAdmin, action), null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var row = (await AuditRowsAsync(auditAction)).Should().ContainSingle().Subject;
        row.OrganisationId.Should().Be(_otherOrganisation.Id);

        AuthenticateAs(Role.OrganisationAdmin, _secondOrganisationAdmin.UserId.Value, _mainOrganisation.Id.Value);
        (await AuditLogAsync(auditAction)).Items.Should().BeEmpty();

        AuthenticateAs(Role.OrganisationAdmin, otherAdmin.UserId.Value, _otherOrganisation.Id.Value);
        (await AuditLogAsync(auditAction)).Items.Should().ContainSingle().Which.Id.Should().Be(row.Id.Value);
    }

    [Fact]
    public async Task ShouldDeleteOrganisationUser_AndAuditItAgainstTheOrganisation()
    {
        // Arrange — a Lead, so the last-LA-admin guard does not apply, with an enrolment and a live session.
        var lead = OrganisationUsersData.Member(_mainOrganisation.Id, UserRole.Lead, "deleted.lead@main-organisation.gov.uk");
        await Context.OrganisationUsers.AddAsync(lead);
        await Context.UserMfas.AddAsync(MfaData.Enabled(lead.UserId));
        await Context.MfaSessions.AddAsync(MfaData.Session(lead.UserId));
        await SaveChangesAsync();

        AuthenticateAs(Role.OrganisationAdmin, _secondOrganisationAdmin.UserId.Value, _mainOrganisation.Id.Value);

        // Act
        var response = await Client.DeleteAsync(UserRoute(_mainOrganisation, lead));

        // Assert — a hard delete: the user, the membership and everything hanging off the user are gone.
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Context.Users.AsNoTracking().AnyAsync(x => x.Id == lead.UserId)).Should().BeFalse();
        (await Context.OrganisationUsers.AsNoTracking().AnyAsync(x => x.UserId == lead.UserId)).Should().BeFalse();
        (await Context.UserMfas.AsNoTracking().AnyAsync(x => x.UserId == lead.UserId)).Should().BeFalse();
        (await Context.MfaSessions.AsNoTracking().AnyAsync(x => x.UserId == lead.UserId)).Should().BeFalse();

        var row = (await AuditRowsAsync(AuditAction.UserDeleted)).Should().ContainSingle().Subject;
        row.ActorUserId.Should().Be(_secondOrganisationAdmin.UserId);
        row.TargetId.Should().Be(lead.UserId.Value);
        row.OrganisationId.Should().Be(_mainOrganisation.Id);
        row.After.Should().BeNull();

        // The audit row is all that is left of the user, and it is what the LA's "Deleted users" pages are
        // built from: the list by action, then the snapshot by id. The target has no name to resolve any more.
        var listed = (await AuditLogAsync(AuditAction.UserDeleted, "&pageSize=200")).Items.Should().ContainSingle().Subject;
        listed.Id.Should().Be(row.Id.Value);
        listed.TargetDisplayName.Should().BeNull();

        var detailResponse = await Client.GetAsync($"organisation-admin/audit-logs/{row.Id.Value}");
        detailResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = await detailResponse.ToResponseModel<AuditLogDetailDto>();

        using var before = JsonDocument.Parse(detail.Before!);
        before.RootElement.GetProperty("firstName").GetString().Should().Be(lead.User!.Name.FirstName);
        before.RootElement.GetProperty("lastName").GetString().Should().Be(lead.User.Name.LastName);
        before.RootElement.GetProperty("email").GetString().Should().Be(lead.User.Email);
        before.RootElement.GetProperty("role").GetString().Should().Be(Role.Lead);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ShouldNotDeleteAnotherOrganisationsUser(bool routeNamesTheTargetsOrganisation)
    {
        // Arrange
        var otherAdmin = OtherOrganisationAdmin();
        await Context.Organisations.AddAsync(_otherOrganisation);
        await Context.OrganisationUsers.AddAsync(otherAdmin);
        await SaveChangesAsync();

        AuthenticateAs(Role.OrganisationAdmin, _secondOrganisationAdmin.UserId.Value, _mainOrganisation.Id.Value);

        // Act
        var routeOrganisation = routeNamesTheTargetsOrganisation ? _otherOrganisation : _mainOrganisation;
        var response = await Client.DeleteAsync(UserRoute(routeOrganisation, otherAdmin));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Context.Users.AsNoTracking().AnyAsync(x => x.Id == otherAdmin.UserId)).Should().BeTrue();
        (await Context.OrganisationUsers.AsNoTracking().AnyAsync(x => x.UserId == otherAdmin.UserId)).Should().BeTrue();
        (await AuditRowsAsync(AuditAction.UserDeleted)).Should().BeEmpty();
    }

    [Fact]
    public async Task ShouldRefuseToDeleteTheOnlyActiveOrganisationAdmin()
    {
        // Act — the seeded user is the organisation's one admin; deleting it would leave nobody able to
        // manage the LA's users.
        var response = await Client.DeleteAsync(UserRoute(_mainOrganisation, _secondOrganisationAdmin));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await Context.Users.AsNoTracking().AnyAsync(x => x.Id == _secondOrganisationAdmin.UserId)).Should().BeTrue();
        (await Context.OrganisationUsers.AsNoTracking().AnyAsync(x => x.UserId == _secondOrganisationAdmin.UserId)).Should().BeTrue();
        (await AuditRowsAsync(AuditAction.UserDeleted)).Should().BeEmpty();
    }

    [Fact]
    public async Task ShouldRefuseEnablePasswordLogin_WhenEmailNotConfigured_AndLeaveUserUntouched()
    {
        InMemoryTemporaryPasswordEmailService.NotConfigure();

        var response = await Client.PostAsync(
            $"organisations/{_mainOrganisation.Id.Value}/users/{_secondOrganisationAdmin.UserId.Value}/enable-password-login", null);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        // The guard must fire BEFORE Entra/the domain state is touched — not just report an error afterwards.
        var saved = await Context.Users.AsNoTracking().FirstAsync(x => x.Id == _secondOrganisationAdmin.UserId);
        saved.PasswordLoginEnabled.Should().BeFalse();
        _temporaryPasswordEmails.Sent.Should().NotContain(s => s.Email == _secondOrganisationAdmin.User!.Email);

        (await Context.AuditLogs.AsNoTracking()
                .AnyAsync(x => x.TargetType == AuditTargetType.User && x.TargetId == _secondOrganisationAdmin.UserId.Value && x.Action == AuditAction.UserPasswordLoginEnabled))
            .Should().BeFalse();
    }

    [Fact]
    public async Task ShouldReportFailure_WhenEnablePasswordLoginEmailFailsAfterEntraSucceeded()
    {
        InMemoryTemporaryPasswordEmailService.Fail();

        var response = await Client.PostAsync(
            $"organisations/{_mainOrganisation.Id.Value}/users/{_secondOrganisationAdmin.UserId.Value}/enable-password-login", null);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        // Unlike the not-configured case: Entra/the domain state and the audit trail already happened by the
        // time the send failed, and must not be rolled back — only the response must say so.
        var saved = await Context.Users.AsNoTracking().FirstAsync(x => x.Id == _secondOrganisationAdmin.UserId);
        saved.PasswordLoginEnabled.Should().BeTrue();

        (await Context.AuditLogs.AsNoTracking()
                .AnyAsync(x => x.TargetType == AuditTargetType.User && x.TargetId == _secondOrganisationAdmin.UserId.Value && x.Action == AuditAction.UserPasswordLoginEnabled))
            .Should().BeTrue();
    }

    [Fact]
    public async Task ShouldResetPassword_SendEmail_AndWriteAudit()
    {
        var orgUser = PasswordLoginEnabledOrgUser();
        await Context.OrganisationUsers.AddAsync(orgUser);
        await SaveChangesAsync();

        var response = await Client.PostAsync(
            $"organisations/{_mainOrganisation.Id.Value}/users/{orgUser.UserId.Value}/reset-password", null);

        response.IsSuccessStatusCode.Should().BeTrue();
        _temporaryPasswordEmails.Sent.Should().Contain(s => s.Email == orgUser.User!.Email);

        (await Context.AuditLogs.AsNoTracking()
                .AnyAsync(x => x.TargetType == AuditTargetType.User && x.TargetId == orgUser.UserId.Value && x.Action == AuditAction.UserPasswordReset))
            .Should().BeTrue();
    }

    [Fact]
    public async Task ShouldRefuseResetPassword_WhenEmailNotConfigured()
    {
        var orgUser = PasswordLoginEnabledOrgUser();
        await Context.OrganisationUsers.AddAsync(orgUser);
        await SaveChangesAsync();
        InMemoryTemporaryPasswordEmailService.NotConfigure();

        var response = await Client.PostAsync(
            $"organisations/{_mainOrganisation.Id.Value}/users/{orgUser.UserId.Value}/reset-password", null);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        _temporaryPasswordEmails.Sent.Should().NotContain(s => s.Email == orgUser.User!.Email);

        (await Context.AuditLogs.AsNoTracking()
                .AnyAsync(x => x.TargetType == AuditTargetType.User && x.TargetId == orgUser.UserId.Value && x.Action == AuditAction.UserPasswordReset))
            .Should().BeFalse();
    }

    [Fact]
    public async Task ShouldReportFailure_WhenResetPasswordEmailFailsAfterEntraSucceeded()
    {
        var orgUser = PasswordLoginEnabledOrgUser();
        await Context.OrganisationUsers.AddAsync(orgUser);
        await SaveChangesAsync();
        InMemoryTemporaryPasswordEmailService.Fail();

        var response = await Client.PostAsync(
            $"organisations/{_mainOrganisation.Id.Value}/users/{orgUser.UserId.Value}/reset-password", null);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        // The reset already happened in Entra and every session was already revoked — the audit row proves it
        // — even though the response correctly reports the send failure rather than success.
        (await Context.AuditLogs.AsNoTracking()
                .AnyAsync(x => x.TargetType == AuditTargetType.User && x.TargetId == orgUser.UserId.Value && x.Action == AuditAction.UserPasswordReset))
            .Should().BeTrue();
    }

    // ResetUserPasswordCommand refuses a user who never had password login enabled in the first
    // place (PasswordLoginNotEnabledException), so every reset test needs a target already past that gate.
    private OrganisationUser PasswordLoginEnabledOrgUser()
    {
        var orgUser = OrganisationUsersData.Member(_mainOrganisation.Id, UserRole.Lead, "password.org.user@main-organisation.gov.uk");
        orgUser.User!.EnablePasswordLogin(DateTime.UtcNow);

        return orgUser;
    }

    private static string UserRoute(Organisation organisation, OrganisationUser user, string? action = null) =>
        $"organisations/{organisation.Id.Value}/users/{user.UserId.Value}" + (action is null ? string.Empty : $"/{action}");

    // An LA admin in a second organisation — the one to look from when a test is about what another LA can
    // see or reach. Its organisation is not seeded by InitializeAsync, so the test adds both.
    private OrganisationUser OtherOrganisationAdmin() =>
        OrganisationUsersData.Member(_otherOrganisation.Id, UserRole.OrganisationAdmin, "admin@second-council.gov.uk");

    private Task<List<AuditLog>> AuditRowsAsync(string action) =>
        Context.AuditLogs.AsNoTracking().Where(x => x.Action == action).ToListAsync();

    // What the caller's own LA audit log answers for one action. Run as whoever the test last authenticated as.
    private async Task<PagedAuditLogs<AuditLogListItemDto>> AuditLogAsync(string action, string extraQuery = "")
    {
        var response = await Client.GetAsync($"organisation-admin/audit-logs?action={action}{extraQuery}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return await response.ToResponseModel<PagedAuditLogs<AuditLogListItemDto>>();
    }

    public async Task InitializeAsync()
    {
        await Context.Organisations.AddAsync(_mainOrganisation);
        await Context.OrganisationUsers.AddAsync(_secondOrganisationAdmin);

        await SaveChangesAsync();
        Context.ChangeTracker.Clear();

        _temporaryPasswordEmails.Clear();
    }

    public async Task DisposeAsync()
    {
        Context.AuditLogs.RemoveRange(Context.AuditLogs);
        Context.MfaSessions.RemoveRange(Context.MfaSessions);
        Context.UserMfas.RemoveRange(Context.UserMfas);
        Context.OrganisationUsers.RemoveRange(Context.OrganisationUsers);
        Context.Users.RemoveRange(Context.Users);
        Context.Organisations.RemoveRange(Context.Organisations);

        await SaveChangesAsync();
        AuthenticateAsAdmin();
    }
}