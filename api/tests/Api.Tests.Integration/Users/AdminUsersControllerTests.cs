using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Dtos;
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

namespace Api.Tests.Integration.Users;

public class AdminUsersControllerTests : BaseIntegrationTest, IAsyncLifetime
{
    private const string AdminUsersRoute = "/admin/users";

    private readonly InMemoryTemporaryPasswordEmailService _temporaryPasswordEmails;

    public AdminUsersControllerTests(IntegrationTestWebFactory factory) : base(factory)
    {
        _temporaryPasswordEmails = factory.Services.GetRequiredService<InMemoryTemporaryPasswordEmailService>();
    }

    public async Task InitializeAsync()
    {
        // Reset seeded data + the in-memory invitation channel for each test.
        // Clear join rows first — organisation_users / location_users hold ON DELETE RESTRICT FKs to users.
        await ClearFixturesAsync();

        await Context.Users.AddAsync(AdminsData.MainAdmin);
        await SaveChangesAsync();

        // InMemoryInvitationEmailService is deliberately not cleared: its bag is static and shared with classes running
        // in parallel, so clearing it here wiped their sends mid-test. Every assertion against it matches by address.
        // The temporary-password recorder is this host's own instance, so clearing it touches no other class.
        _temporaryPasswordEmails.Clear();
    }

    public async Task DisposeAsync()
    {
        await RejectedInserts.RestoreAsync(Context);
        await ClearFixturesAsync();
    }

    private async Task ClearFixturesAsync()
    {
        // Use the base SaveChangesAsync() so the change tracker is cleared between saves — the API runs
        // in-process on a separate DbContext, so a stale tracker here would clash with its deletes.
        Context.OrganisationUsers.RemoveRange(Context.OrganisationUsers);
        Context.LocationUsers.RemoveRange(Context.LocationUsers);
        await SaveChangesAsync();

        Context.Users.RemoveRange(Context.Users);
        Context.Locations.RemoveRange(Context.Locations);
        Context.Organisations.RemoveRange(Context.Organisations);
        Context.AuditLogs.RemoveRange(Context.AuditLogs);
        await SaveChangesAsync();
    }

    // Posts CreateUserPayload rather than UserDto. UserDto is the *response* shape and carries Id, Status,
    // Role and OrganisationName, none of which this endpoint declares — they were silently discarded until
    // BLUM-7240 made an undeclared field a 400. The old assertion that Status came back "active" was
    // therefore passing on the default, not on anything the request said.
    [Fact]
    public async Task ShouldCreateUser_WriteAuditLog_AndSendInvitation()
    {
        var request = new CreateUserPayload(
            FirstName: "New",
            LastName: "Admin",
            Email: "new.admin@example.com",
            IsActive: true);

        var response = await Client.PostAsJsonAsync($"{AdminUsersRoute}/create", request);

        response.IsSuccessStatusCode.Should().BeTrue();
        var created = await response.ToResponseModel<UserDto>();
        created.Status.Should().Be("active");

        var dbUser = await Context.Users.AsNoTracking().FirstAsync(x => x.Email == request.Email);
        dbUser.Status.ToString().Should().Be("active");

        InMemoryInvitationEmailService.Sent.Should().Contain(s => s.Email == request.Email);

        var audit = await Context.AuditLogs.AsNoTracking()
            .Where(x => x.TargetType == AuditTargetType.User && x.TargetId == dbUser.Id.Value)
            .ToListAsync();
        audit.Should().ContainSingle(x => x.Action == AuditAction.UserCreated);
        audit[0].ActorUserId.Should().Be(AdminsData.MainAdmin.Id);
    }

    [Fact]
    public async Task ShouldCreateDeactivatedUser_WhenActivationIsDeferred()
    {
        var request = new
        {
            FirstName = "Deferred",
            LastName = "Admin",
            Email = "deferred.admin@example.com",
            IsActive = false,
        };

        var response = await Client.PostAsJsonAsync($"{AdminUsersRoute}/create", request);

        response.IsSuccessStatusCode.Should().BeTrue();
        var created = await response.ToResponseModel<UserDto>();
        created.Status.Should().Be("deactivated");

        var dbUser = await Context.Users.AsNoTracking().FirstAsync(x => x.Email == request.Email);
        dbUser.Status.Should().Be(UserStatus.Deactivated);
    }

    [Fact]
    public async Task ShouldRejectDuplicateEmailOnCreate()
    {
        // Arrange: pre-seed a second admin with a known email
        await Context.Users.AddAsync(AdminsData.SecondAdmin);
        await Context.SaveChangesAsync();

        var request = new CreateUserPayload(
            FirstName: "Another",
            LastName: "Admin",
            Email: AdminsData.SecondAdmin.Email,
            IsActive: true);

        var response = await Client.PostAsJsonAsync($"{AdminUsersRoute}/create", request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ShouldDeactivateUser_WriteAudit_AndFlipStatus()
    {
        await Context.Users.AddAsync(AdminsData.SecondAdmin);
        await Context.SaveChangesAsync();

        var response = await Client.PostAsync($"{AdminUsersRoute}/{AdminsData.SecondAdmin.Id.Value}/deactivate", null);

        response.IsSuccessStatusCode.Should().BeTrue();
        var dto = await response.ToResponseModel<UserDto>();
        dto.Status.Should().Be("deactivated");

        var dbUser = await Context.Users.AsNoTracking().FirstAsync(x => x.Id == AdminsData.SecondAdmin.Id);
        dbUser.Status.Should().Be(UserStatus.Deactivated);

        (await Context.AuditLogs.AsNoTracking()
                .AnyAsync(x => x.TargetType == AuditTargetType.User && x.TargetId == AdminsData.SecondAdmin.Id.Value && x.Action == AuditAction.UserDeactivated))
            .Should().BeTrue();
    }

    [Fact]
    public async Task ShouldRejectSelfDeactivation()
    {
        var response = await Client.PostAsync($"{AdminUsersRoute}/{AdminsData.MainAdmin.Id.Value}/deactivate", null);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task ShouldRejectDeactivatingOnlyAdmin()
    {
        // Arrange — a deactivated peer does not count, so MainAdmin is the only active admin. The caller is a DHSC
        // admin who is not in the database, so the self-refusal cannot answer before the only-admin one.
        await Context.Users.AddAsync(AdminsData.DeactivatedAdmin);
        await SaveChangesAsync();
        AuthenticateAs(Role.Admin, Guid.NewGuid(), Guid.NewGuid());

        // Act
        var response = await Client.PostAsync($"{AdminUsersRoute}/{AdminsData.MainAdmin.Id.Value}/deactivate", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await Context.Users.AsNoTracking().FirstAsync(x => x.Id == AdminsData.MainAdmin.Id)).Status.Should().Be(UserStatus.Active);
        (await Context.AuditLogs.AsNoTracking().AnyAsync(x => x.Action == AuditAction.UserDeactivated)).Should().BeFalse();
        AuthenticateAsAdmin();
    }

    [Fact]
    public async Task ShouldReactivateUser_WriteAudit_AndNotInvalidateMfa()
    {
        await Context.Users.AddAsync(AdminsData.DeactivatedAdmin);
        await Context.SaveChangesAsync();

        var response = await Client.PostAsync($"{AdminUsersRoute}/{AdminsData.DeactivatedAdmin.Id.Value}/reactivate", null);

        response.IsSuccessStatusCode.Should().BeTrue();
        var dto = await response.ToResponseModel<UserDto>();
        dto.Status.Should().Be("active");

        (await Context.AuditLogs.AsNoTracking()
                .AnyAsync(x => x.TargetType == AuditTargetType.User && x.TargetId == AdminsData.DeactivatedAdmin.Id.Value && x.Action == AuditAction.UserReactivated))
            .Should().BeTrue();
    }

    [Fact]
    public async Task ShouldReactivateUser_EvenWhenGraphIsUnavailable()
    {
        // Since BLUM-6386 deactivation never disables the CIAM account, so this Graph call is only a repair
        // for accounts disabled by the old behaviour. Letting its failure fold into UserUnknownException left
        // the DB row deactivated, which meant UserDeactivatedMiddleware kept refusing a user whose CIAM
        // account was fine — an admin unable to undo a deactivation because a directory they no longer depend
        // on was down.
        await Context.Users.AddAsync(AdminsData.DeactivatedAdmin);
        await Context.SaveChangesAsync();
        TestGraphContext.FailEnable();

        try
        {
            var response = await Client.PostAsync($"{AdminUsersRoute}/{AdminsData.DeactivatedAdmin.Id.Value}/reactivate", null);

            response.IsSuccessStatusCode.Should().BeTrue();
            var saved = await Context.Users.AsNoTracking().FirstAsync(x => x.Id == AdminsData.DeactivatedAdmin.Id);
            saved.Status.Should().Be(UserStatus.Active, "local status is the source of truth for access, not the CIAM account");
        }
        finally
        {
            TestGraphContext.Reset();
        }
    }

    [Fact]
    public async Task ShouldReportFailure_AndCreateNothing_WhenTheDirectoryLookupFails()
    {
        // BLUM-8746 — a lookup Graph refused used to escape the handler as an unhandled exception. It is now
        // returned, so the request is answered through UserErrorFactory, and the handler must not go on to
        // create an account for an address it could not look up.
        var request = new CreateUserPayload(
            FirstName: "Lookup",
            LastName: "Fails",
            Email: "lookup.fails@example.com",
            IsActive: true);
        TestGraphContext.FailFindByEmail();

        try
        {
            var response = await Client.PostAsJsonAsync($"{AdminUsersRoute}/create", request);

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
    public async Task ShouldHardDeleteUser_AndPreserveAuditRow()
    {
        await Context.Users.AddAsync(AdminsData.SecondAdmin);
        await Context.SaveChangesAsync();

        var response = await Client.DeleteAsync($"{AdminUsersRoute}/{AdminsData.SecondAdmin.Id.Value}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var dbUser = await Context.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == AdminsData.SecondAdmin.Id);
        dbUser.Should().BeNull();

        // Audit row survives despite hard-deleted user (no FK from audit_logs.target_user_id)
        var audit = await Context.AuditLogs.AsNoTracking()
            .Where(x => x.TargetType == AuditTargetType.User && x.TargetId == AdminsData.SecondAdmin.Id.Value && x.Action == AuditAction.UserDeleted)
            .ToListAsync();
        audit.Should().ContainSingle();
    }

    [Fact]
    public async Task ShouldRejectSelfDelete()
    {
        var response = await Client.DeleteAsync($"{AdminUsersRoute}/{AdminsData.MainAdmin.Id.Value}");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    // BLUM-8161 review: a failed temporary-password email must not report success — see
    // EnableUserPasswordLoginCommand for why. These three cover the happy path plus both failure shapes the
    // handler now distinguishes: refused before Entra is touched, versus failed after.
    [Fact]
    public async Task ShouldEnablePasswordLogin_SendEmail_AndWriteAudit()
    {
        await Context.Users.AddAsync(AdminsData.SecondAdmin);
        await SaveChangesAsync();

        var response = await Client.PostAsync($"{AdminUsersRoute}/{AdminsData.SecondAdmin.Id.Value}/enable-password-login", null);

        response.IsSuccessStatusCode.Should().BeTrue();

        var saved = await Context.Users.AsNoTracking().FirstAsync(x => x.Id == AdminsData.SecondAdmin.Id);
        saved.PasswordLoginEnabled.Should().BeTrue();

        _temporaryPasswordEmails.Sent.Should().Contain(s => s.Email == AdminsData.SecondAdmin.Email);

        (await Context.AuditLogs.AsNoTracking()
                .AnyAsync(x => x.TargetType == AuditTargetType.User && x.TargetId == AdminsData.SecondAdmin.Id.Value && x.Action == AuditAction.UserPasswordLoginEnabled))
            .Should().BeTrue();
    }

    [Fact]
    public async Task ShouldRefuseEnablePasswordLogin_WhenEmailNotConfigured_AndLeaveUserUntouched()
    {
        await Context.Users.AddAsync(AdminsData.SecondAdmin);
        await SaveChangesAsync();
        InMemoryTemporaryPasswordEmailService.NotConfigure();

        var response = await Client.PostAsync($"{AdminUsersRoute}/{AdminsData.SecondAdmin.Id.Value}/enable-password-login", null);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        // The guard must fire BEFORE Entra/the domain state is touched — not just report an error afterwards.
        var saved = await Context.Users.AsNoTracking().FirstAsync(x => x.Id == AdminsData.SecondAdmin.Id);
        saved.PasswordLoginEnabled.Should().BeFalse();
        _temporaryPasswordEmails.Sent.Should().NotContain(s => s.Email == AdminsData.SecondAdmin.Email);

        (await Context.AuditLogs.AsNoTracking()
                .AnyAsync(x => x.TargetType == AuditTargetType.User && x.TargetId == AdminsData.SecondAdmin.Id.Value && x.Action == AuditAction.UserPasswordLoginEnabled))
            .Should().BeFalse();
    }

    [Fact]
    public async Task ShouldReportFailure_WhenEnablePasswordLoginEmailFailsAfterEntraSucceeded()
    {
        await Context.Users.AddAsync(AdminsData.SecondAdmin);
        await SaveChangesAsync();
        InMemoryTemporaryPasswordEmailService.Fail();

        var response = await Client.PostAsync($"{AdminUsersRoute}/{AdminsData.SecondAdmin.Id.Value}/enable-password-login", null);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        // Unlike the not-configured case: Entra/the domain state and the audit trail already happened by the
        // time the send failed, and must not be rolled back — only the response must say so.
        var saved = await Context.Users.AsNoTracking().FirstAsync(x => x.Id == AdminsData.SecondAdmin.Id);
        saved.PasswordLoginEnabled.Should().BeTrue();

        (await Context.AuditLogs.AsNoTracking()
                .AnyAsync(x => x.TargetType == AuditTargetType.User && x.TargetId == AdminsData.SecondAdmin.Id.Value && x.Action == AuditAction.UserPasswordLoginEnabled))
            .Should().BeTrue();
    }

    [Fact]
    public async Task ShouldKeepTheRecreatedAccountsSubId_WhenTheAuditRowCannotBeWritten()
    {
        // PR #87 review: when Entra deletes and recreates the account, the old object id stops resolving at once.
        // Committing the new one together with the audit row meant a failed audit insert rolled users.sub_id back
        // to the deleted account — no email, and every retry sent Entra the dead id, locking the user out for good.
        await Context.Users.AddAsync(AdminsData.SecondAdmin);
        await SaveChangesAsync();
        var recreated = Guid.NewGuid();
        await RejectedInserts.RejectAuditAsync(Context, AuditAction.UserPasswordLoginEnabled);

        HttpResponseMessage response;
        TestGraphContext.RecreateAccountOnEnablePasswordLogin(recreated);
        try
        {
            response = await Client.PostAsync($"{AdminUsersRoute}/{AdminsData.SecondAdmin.Id.Value}/enable-password-login", null);
        }
        finally
        {
            TestGraphContext.Reset();
        }

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError, "the revocation and its audit row did not commit, so it is not a success");

        var saved = await Context.Users.AsNoTracking().FirstAsync(x => x.Id == AdminsData.SecondAdmin.Id);
        saved.SubId.Value.Should().Be(recreated, "the row must keep pointing at the account Entra now has");
        saved.PasswordLoginEnabled.Should().BeTrue();

        // And the admin's retry reaches that live account and completes.
        await RejectedInserts.RestoreAsync(Context);
        var retry = await Client.PostAsync($"{AdminUsersRoute}/{AdminsData.SecondAdmin.Id.Value}/enable-password-login", null);

        retry.IsSuccessStatusCode.Should().BeTrue();
        _temporaryPasswordEmails.Sent.Should().Contain(s => s.Email == AdminsData.SecondAdmin.Email);
    }

    [Fact]
    public async Task ShouldResetPassword_SendEmail_AndWriteAudit()
    {
        var user = PasswordLoginEnabledUser();
        await Context.Users.AddAsync(user);
        await SaveChangesAsync();

        var response = await Client.PostAsync($"{AdminUsersRoute}/{user.Id.Value}/reset-password", null);

        response.IsSuccessStatusCode.Should().BeTrue();
        _temporaryPasswordEmails.Sent.Should().Contain(s => s.Email == user.Email);

        (await Context.AuditLogs.AsNoTracking()
                .AnyAsync(x => x.TargetType == AuditTargetType.User && x.TargetId == user.Id.Value && x.Action == AuditAction.UserPasswordReset))
            .Should().BeTrue();
    }

    [Fact]
    public async Task ShouldRefuseResetPassword_WhenEmailNotConfigured()
    {
        var user = PasswordLoginEnabledUser();
        await Context.Users.AddAsync(user);
        await SaveChangesAsync();
        InMemoryTemporaryPasswordEmailService.NotConfigure();

        var response = await Client.PostAsync($"{AdminUsersRoute}/{user.Id.Value}/reset-password", null);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        _temporaryPasswordEmails.Sent.Should().NotContain(s => s.Email == user.Email);

        (await Context.AuditLogs.AsNoTracking()
                .AnyAsync(x => x.TargetType == AuditTargetType.User && x.TargetId == user.Id.Value && x.Action == AuditAction.UserPasswordReset))
            .Should().BeFalse();
    }

    [Fact]
    public async Task ShouldReportFailure_WhenResetPasswordEmailFailsAfterEntraSucceeded()
    {
        var user = PasswordLoginEnabledUser();
        await Context.Users.AddAsync(user);
        await SaveChangesAsync();
        InMemoryTemporaryPasswordEmailService.Fail();

        var response = await Client.PostAsync($"{AdminUsersRoute}/{user.Id.Value}/reset-password", null);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        // The reset already happened in Entra and every session was already revoked — the audit row proves it
        // — even though the response correctly reports the send failure rather than success.
        (await Context.AuditLogs.AsNoTracking()
                .AnyAsync(x => x.TargetType == AuditTargetType.User && x.TargetId == user.Id.Value && x.Action == AuditAction.UserPasswordReset))
            .Should().BeTrue();
    }

    // ResetUserPasswordCommand refuses a user who never had password login enabled in the first place
    // (PasswordLoginNotEnabledException), so every reset test needs a target already past that gate.
    private static User PasswordLoginEnabledUser()
    {
        var user = User.New(
            id: UserId.New(),
            name: new Name("Password", "User"),
            email: "password.user@example.com",
            subId: new SubId(Guid.NewGuid()),
            status: UserStatus.Active,
            role: UserRole.Admin,
            utcNow: DateTime.UtcNow);
        user.EnablePasswordLogin(DateTime.UtcNow);

        return user;
    }

    [Fact]
    public async Task ShouldHardDeleteOrganisationUser_AndClearJoinRow()
    {
        // Arrange: a user with an organisation_users join row (ON DELETE RESTRICT FK). A Lead, because the
        // organisation's only LA Admin is protected by the last-active-admin guard on this route too (BLUM-8751).
        var organisation = Organisation.New(
            OrganisationId.New(),
            "Delete Org User Council",
            "E06000060",
            Region.London,
            OrgType.LocalAuthority,
            "E1000001",
            OnboardingStatus.Invited,
            DateTime.UtcNow);
        var orgUser = OrganisationUsersData.Member(organisation.Id, UserRole.Lead, "deleted.lead@council.gov.uk");
        var userId = orgUser.UserId;

        await Context.Organisations.AddAsync(organisation);
        await Context.OrganisationUsers.AddAsync(orgUser);
        await SaveChangesAsync();

        // Act
        var response = await Client.DeleteAsync($"{AdminUsersRoute}/{userId.Value}");

        // Assert — the delete succeeds and both the user and its join row are gone
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await Context.Users.AsNoTracking().AnyAsync(x => x.Id == userId)).Should().BeFalse();
        (await Context.OrganisationUsers.AsNoTracking().AnyAsync(x => x.UserId == userId)).Should().BeFalse();

        var audit = await Context.AuditLogs.AsNoTracking()
            .Where(x => x.TargetType == AuditTargetType.User && x.TargetId == userId.Value && x.Action == AuditAction.UserDeleted)
            .ToListAsync();
        audit.Should().ContainSingle().Which.OrganisationId.Should().Be(organisation.Id);
    }

    [Fact]
    public async Task ShouldHardDeleteLocationUser_AndClearJoinRow()
    {
        // Arrange: a user with a location_users join row (ON DELETE RESTRICT FK)
        var organisation = Organisation.New(
            OrganisationId.New(),
            "Delete Loc User Council",
            "E06000061",
            Region.London,
            OrgType.LocalAuthority,
            "E1000001",
            OnboardingStatus.Invited,
            DateTime.UtcNow);
        var location = LocationsData.MainLocation(organisation.Id);
        var locationUser = LocationUsersData.MainLocationUser(location.Id);
        var userId = locationUser.UserId;

        await Context.Organisations.AddAsync(organisation);
        await Context.Locations.AddAsync(location);
        await Context.LocationUsers.AddAsync(locationUser);
        await SaveChangesAsync();

        // Act
        var response = await Client.DeleteAsync($"{AdminUsersRoute}/{userId.Value}");

        // Assert — the delete succeeds and both the user and its join row are gone
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await Context.Users.AsNoTracking().AnyAsync(x => x.Id == userId)).Should().BeFalse();
        (await Context.LocationUsers.AsNoTracking().AnyAsync(x => x.UserId == userId)).Should().BeFalse();

        var audit = await Context.AuditLogs.AsNoTracking()
            .Where(x => x.TargetType == AuditTargetType.User && x.TargetId == userId.Value && x.Action == AuditAction.UserDeleted)
            .ToListAsync();
        audit.Should().ContainSingle();
    }

    // BLUM-7038: the DHSC edit screen can now assign "LA lead", so a DHSC admin can demote an org admin.
    // AdminEditUserCommand must refuse to strand an LA with zero active org admins, through the same
    // LastActiveAdminGuard the org-scoped UpdateOrganisationUserCommand uses (BLUM-8751).
    [Fact]
    public async Task ShouldBlockDemotingTheOnlyActiveOrgAdmin_ToLead()
    {
        var organisation = OrganisationsData.MainOrganisation;
        var orgAdmin = OrganisationUsersData.MainOrganisationUser(organisation.Id);
        await Context.Organisations.AddAsync(organisation);
        await Context.OrganisationUsers.AddAsync(orgAdmin);
        await SaveChangesAsync();

        var response = await Client.PutAsJsonAsync(
            $"{AdminUsersRoute}/edit",
            EditPayload(orgAdmin, Role.Lead, organisation.Id.Value));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var saved = await Context.Users.AsNoTracking().FirstAsync(x => x.Id == orgAdmin.UserId);
        saved.Role.Should().Be(UserRole.OrganisationAdmin);
    }

    // Promoting the sole org admin to DHSC Admin strips their LA membership, which strands the LA just as a
    // demotion does — so it must be blocked on the same grounds.
    [Fact]
    public async Task ShouldBlockPromotingTheOnlyActiveOrgAdmin_ToDhscAdmin()
    {
        var organisation = OrganisationsData.MainOrganisation;
        var orgAdmin = OrganisationUsersData.MainOrganisationUser(organisation.Id);
        await Context.Organisations.AddAsync(organisation);
        await Context.OrganisationUsers.AddAsync(orgAdmin);
        await SaveChangesAsync();

        var response = await Client.PutAsJsonAsync(
            $"{AdminUsersRoute}/edit",
            EditPayload(orgAdmin, Role.Admin, organisationId: null));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var saved = await Context.Users.AsNoTracking().FirstAsync(x => x.Id == orgAdmin.UserId);
        saved.Role.Should().Be(UserRole.OrganisationAdmin);
    }

    // The guard must not over-fire: demoting one of two org admins leaves the LA covered, so it is allowed.
    [Fact]
    public async Task ShouldAllowDemotingAnOrgAdmin_WhenAnotherActiveOrgAdminRemains()
    {
        var organisation = OrganisationsData.MainOrganisation;
        var firstAdmin = OrganisationUsersData.MainOrganisationUser(organisation.Id);
        var secondAdmin = OrganisationUsersData.SecondOrganisationUser(organisation.Id);
        await Context.Organisations.AddAsync(organisation);
        await Context.OrganisationUsers.AddAsync(firstAdmin);
        await Context.OrganisationUsers.AddAsync(secondAdmin);
        await SaveChangesAsync();

        var response = await Client.PutAsJsonAsync(
            $"{AdminUsersRoute}/edit",
            EditPayload(firstAdmin, Role.Lead, organisation.Id.Value));

        response.IsSuccessStatusCode.Should().BeTrue();
        var saved = await Context.Users.AsNoTracking().FirstAsync(x => x.Id == firstAdmin.UserId);
        saved.Role.Should().Be(UserRole.Lead);
    }

    [Fact]
    public async Task ShouldCountAdminsOfEveryStatus_AndEveryOrganisation()
    {
        // Arrange
        await SeedAdminsAndTwoOrganisationsAsync();

        // Act
        var response = await Client.GetAsync("admin/totals");

        // Assert — three DHSC admins, the deactivated one included, and neither LA user; two organisations.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.ToResponseModel<AdminTotalsResponse>()).Should().Be(new AdminTotalsResponse(Admins: 3, Organisations: 2));
    }

    [Theory]
    [InlineData(Role.OrganisationAdmin)]
    [InlineData(Role.Lead)]
    public async Task ShouldForbidOrganisationRoles_FromTheDhscHomeTotals(string role)
    {
        AuthenticateAs(role, Guid.NewGuid(), Guid.NewGuid());

        // Act
        var response = await Client.GetAsync("admin/totals");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ShouldResetMfa_RemoveEnrolment_RevokeSessions_AndWriteAudit()
    {
        // Arrange — a target enrolled and signed in on two devices, and a bystander who must be left alone.
        var target = AdminsData.SecondAdmin;
        var bystander = AdminsData.DeactivatedAdmin;
        var targetSessions = new[] { MfaData.Session(target.Id), MfaData.Session(target.Id) };
        var bystanderSession = MfaData.Session(bystander.Id);

        await Context.Users.AddRangeAsync(target, bystander);
        await Context.UserMfas.AddRangeAsync(MfaData.Enabled(target.Id), MfaData.Enabled(bystander.Id));
        await Context.MfaSessions.AddRangeAsync(targetSessions);
        await Context.MfaSessions.AddAsync(bystanderSession);
        await SaveChangesAsync();

        // Act
        var response = await Client.PostAsync($"{AdminUsersRoute}/{target.Id.Value}/reset-mfa", null);

        // Assert — the enrolment is deleted, so the next sign-in sets up again; the sessions are revoked but
        // stay as rows.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.ToResponseModel<UserDto>()).Id.Should().Be(target.Id.Value);

        (await Context.UserMfas.AsNoTracking().AnyAsync(x => x.UserId == target.Id)).Should().BeFalse();
        var sessions = await Context.MfaSessions.AsNoTracking().Where(x => x.UserId == target.Id).ToListAsync();
        sessions.Should().HaveCount(2).And.OnlyContain(x => x.IsRevoked);

        (await Context.UserMfas.AsNoTracking().FirstAsync(x => x.UserId == bystander.Id)).IsEnabled.Should().BeTrue();
        (await Context.MfaSessions.AsNoTracking().FirstAsync(x => x.Id == bystanderSession.Id)).IsRevoked.Should().BeFalse();

        var audit = await Context.AuditLogs.AsNoTracking().ToListAsync();
        var reset = audit.Should().ContainSingle(x => x.Action == AuditAction.UserMfaReset).Subject;
        reset.ActorUserId.Should().Be(AdminsData.MainAdmin.Id);
        reset.TargetType.Should().Be(AuditTargetType.User);
        reset.TargetId.Should().Be(target.Id.Value);
        reset.After.Should().BeNull();

        using var before = JsonDocument.Parse(reset.Before!);
        before.RootElement.GetProperty("id").GetGuid().Should().Be(target.Id.Value);
        before.RootElement.GetProperty("email").GetString().Should().Be(target.Email);
        before.RootElement.GetProperty("role").GetString().Should().Be(Role.Admin);
        before.RootElement.GetProperty("status").GetString().Should().Be(Status.Active);

        // One row per session ended, each naming the session it ended.
        audit.Where(x => x.Action == AuditAction.MfaSessionRevoked)
            .Select(x => x.TargetId)
            .Should()
            .BeEquivalentTo(targetSessions.Select(x => x.Id.Value));
    }

    [Fact]
    public async Task ShouldReturn404_WhenResettingMfaForAnUnknownUser()
    {
        // Act
        var response = await Client.PostAsync($"{AdminUsersRoute}/{Guid.NewGuid()}/reset-mfa", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Context.AuditLogs.AsNoTracking().AnyAsync(x => x.Action == AuditAction.UserMfaReset)).Should().BeFalse();
    }

    [Theory]
    [InlineData("none", "not_set_up")]
    [InlineData("pending", "not_set_up")]
    [InlineData("enabled", "enabled")]
    public async Task ShouldGetUserDetail_ReportingMfaStatus(string enrolment, string expectedMfaStatus)
    {
        // Arrange — an LA user, so the organisation fields are populated too.
        var organisation = OrganisationsData.MainOrganisation;
        var orgUser = OrganisationUsersData.MainOrganisationUser(organisation.Id);
        await Context.Organisations.AddAsync(organisation);
        await Context.OrganisationUsers.AddAsync(orgUser);

        // A setup the user started and never finished leaves a row behind. It is not an enrolment.
        if (enrolment == "pending")
        {
            await Context.UserMfas.AddAsync(MfaData.Pending(orgUser.UserId));
        }
        else if (enrolment == "enabled")
        {
            await Context.UserMfas.AddAsync(MfaData.Enabled(orgUser.UserId));
        }

        await SaveChangesAsync();

        // Act
        var response = await Client.GetAsync($"{AdminUsersRoute}/{orgUser.UserId.Value}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = await response.ToResponseModel<UserDetailDto>();

        detail.MfaStatus.Should().Be(expectedMfaStatus);
        detail.Id.Should().Be(orgUser.UserId.Value);
        detail.Email.Should().Be(orgUser.User!.Email);
        detail.Role.Should().Be(Role.OrganisationAdmin);
        detail.Status.Should().Be(Status.Active);
        detail.OrganisationId.Should().Be(organisation.Id.Value);
        detail.OrganisationName.Should().Be(organisation.Name);
        detail.PasswordLoginEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task ShouldGetUserDetail_WithWhoCreatedAndLastUpdatedIt()
    {
        // Arrange — the history is read from the audit log: the first "created" row, the latest "updated"
        // one. The latest update was made by someone who has since been deleted.
        var target = AdminsData.SecondAdmin;
        var created = new DateTime(2026, 8, 21, 9, 0, 0, DateTimeKind.Utc);
        var firstUpdate = new DateTime(2026, 8, 22, 9, 0, 0, DateTimeKind.Utc);
        var latestUpdate = new DateTime(2026, 8, 23, 9, 0, 0, DateTimeKind.Utc);

        await Context.Users.AddAsync(target);
        await SaveChangesAsync();

        await SeedUserAuditAsync(AdminsData.MainAdmin.Id, target.Id, AuditAction.UserUpdated, firstUpdate);
        await SeedUserAuditAsync(UserId.New(), target.Id, AuditAction.UserUpdated, latestUpdate);
        await SeedUserAuditAsync(AdminsData.MainAdmin.Id, target.Id, AuditAction.UserCreated, created);

        // Act
        var response = await Client.GetAsync($"{AdminUsersRoute}/{target.Id.Value}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = await response.ToResponseModel<UserDetailDto>();

        detail.CreatedBy.Should().Be(new AccountHistoryEntryDto("Main Admin", created));
        detail.LastUpdatedBy.Should().Be(new AccountHistoryEntryDto("Unknown", latestUpdate));
        detail.DeletedBy.Should().BeNull();
        detail.OrganisationId.Should().BeNull();
        detail.OrganisationName.Should().BeNull();
    }

    [Fact]
    public async Task ShouldReturn404_ForAnUnknownUser()
    {
        // Act
        var response = await Client.GetAsync($"{AdminUsersRoute}/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ShouldListEveryUser_WithStatusRoleAndOrganisationName()
    {
        // Arrange
        var (laAdmin, lead) = await SeedAdminsAndTwoOrganisationsAsync();

        // Act
        var response = await Client.GetAsync(AdminUsersRoute);

        // Assert — every user of every kind, in no promised order. Only an LA user has an organisation.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var users = await response.ToResponseModel<List<UserDto>>();

        users.Should().BeEquivalentTo(
        [
            new UserDto(AdminsData.MainAdmin.Id.Value, "Main", "Admin", "main.admin@email.com", Status.Active, Role.Admin, OrganisationName: null),
            new UserDto(AdminsData.SecondAdmin.Id.Value, "Second", "Admin", "second.admin@email.com", Status.Active, Role.Admin, OrganisationName: null),
            new UserDto(AdminsData.DeactivatedAdmin.Id.Value, "Deactivated", "Admin", "deactivated.admin@email.com", Status.Deactivated, Role.Admin, OrganisationName: null),
            new UserDto(laAdmin.UserId.Value, "main organisation", "admin", "main.organisation.admin@org.com", Status.Active, Role.OrganisationAdmin, "Main Council"),
            new UserDto(lead.UserId.Value, "lead@second-council.gov.uk", "member", "lead@second-council.gov.uk", Status.Active, Role.Lead, "Second Council"),
        ]);
    }

    // Three DHSC admins (the main one is seeded by InitializeAsync) and two local authorities with one member
    // each. The counts are literals in the tests above because InitializeAsync empties users and organisations.
    private async Task<(OrganisationUser LaAdmin, OrganisationUser Lead)> SeedAdminsAndTwoOrganisationsAsync()
    {
        var laAdmin = OrganisationUsersData.MainOrganisationUser(OrganisationsData.MainOrganisation.Id);
        var lead = OrganisationUsersData.Member(OrganisationsData.SecondOrganisation.Id, UserRole.Lead, "lead@second-council.gov.uk");

        await Context.Users.AddRangeAsync(AdminsData.SecondAdmin, AdminsData.DeactivatedAdmin);
        await Context.Organisations.AddRangeAsync(OrganisationsData.MainOrganisation, OrganisationsData.SecondOrganisation);
        await Context.OrganisationUsers.AddRangeAsync(laAdmin, lead);
        await SaveChangesAsync();

        return (laAdmin, lead);
    }

    // The instant goes in through the change tracker, as in AdminAuditLogsControllerTests: AuditLog.Create
    // takes no time and the entity has no setter for it.
    private async Task SeedUserAuditAsync(UserId actor, UserId target, string action, DateTime occurredAt)
    {
        var entry = await Context.AuditLogs.AddAsync(
            AuditLog.Create(actor, AuditTargetType.User, target.Value, action, before: null, after: null));

        entry.Property(x => x.OccurredAt).CurrentValue = occurredAt;
        await SaveChangesAsync();
    }

    private static UpdateUserPayload EditPayload(Domain.OrganisationUsers.OrganisationUser orgUser, string role, Guid? organisationId)
        => new(
            Id: orgUser.UserId.Value,
            FirstName: orgUser.User!.Name.FirstName,
            LastName: orgUser.User.Name.LastName,
            Email: orgUser.User.Email,
            Role: role,
            OrganisationId: organisationId);
}