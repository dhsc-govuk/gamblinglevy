using System.Net.Http.Json;
using Api.Dtos;
using Domain.Organisations;
using Domain.Users;
using Domain.ValueObjects;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Tests.Common;
using Tests.Common.Services;
using Tests.Data;
using Xunit;

namespace Api.Tests.Integration.Users;

// BLUM-7945: a user created deactivated is not sent an invitation to a service they cannot sign in to. It is held
// and sent the first time they are reactivated, by whichever of the three reactivation paths does it.
//
// InMemoryInvitationEmailService records into a static bag every parallel class shares, so each test uses an
// email of its own and counts only that address.
public class UserInvitationHoldTests(IntegrationTestWebFactory factory)
    : BaseIntegrationTest(factory), IAsyncLifetime
{
    private readonly Organisation _organisation = OrganisationsData.MainOrganisation;

    // An organisation created with "Leave it deactivated".
    private readonly Organisation _deactivatedOrganisation = Organisation.New(
        id: OrganisationId.New(),
        name: "Deactivated Council",
        onsCode: "E06000003",
        region: Region.London,
        type: OrgType.LocalAuthority,
        eCode: "E1000003",
        onboardingStatus: OnboardingStatus.Suspended,
        utcNow: DateTime.UtcNow);

    [Fact]
    public async Task ShouldHoldTheInvitation_WhenAnOrganisationUserIsCreatedDeactivated()
    {
        const string email = "held.on.create@invitation-hold.gov.uk";

        var created = await CreateOrganisationUserAsync(email, "deactivated");

        InvitationsSentTo(email).Should().Be(0);
        var saved = await Context.Users.AsNoTracking().FirstAsync(x => x.Id == new UserId(created.Id!.Value));
        saved.InvitationHeldAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ShouldSendTheHeldInvitationOnce_WhenTheOrganisationUserIsFirstReactivated()
    {
        const string email = "released.on.reactivate@invitation-hold.gov.uk";
        var created = await CreateOrganisationUserAsync(email, "deactivated");

        var reactivate = await Client.PostAsync(OrganisationUserRoute(created.Id!.Value, "reactivate"), null);
        reactivate.IsSuccessStatusCode.Should().BeTrue();

        InvitationsSentTo(email).Should().Be(1);
        var saved = await Context.Users.AsNoTracking().FirstAsync(x => x.Id == new UserId(created.Id.Value));
        saved.InvitationHeldAt.Should().BeNull();

        // Released means sent: a later deactivate/reactivate cycle is an ordinary one and invites nobody.
        (await Client.PostAsync(OrganisationUserRoute(created.Id.Value, "deactivate"), null)).IsSuccessStatusCode.Should().BeTrue();
        (await Client.PostAsync(OrganisationUserRoute(created.Id.Value, "reactivate"), null)).IsSuccessStatusCode.Should().BeTrue();

        InvitationsSentTo(email).Should().Be(1);
    }

    [Fact]
    public async Task ShouldSendTheHeldInvitation_WhenTheEditFormSwitchesTheUserToActive()
    {
        const string email = "released.on.edit@invitation-hold.gov.uk";
        var created = await CreateOrganisationUserAsync(email, "deactivated");

        var edit = await Client.PutAsJsonAsync(
            $"organisations/{_organisation.Id.Value}/users/edit",
            created with { Status = "active" });
        edit.IsSuccessStatusCode.Should().BeTrue();

        InvitationsSentTo(email).Should().Be(1);
    }

    [Fact]
    public async Task ShouldHoldAndRelease_ForADhscUserCreatedDeactivated()
    {
        const string email = "dhsc.held@invitation-hold.gov.uk";

        var create = await Client.PostAsJsonAsync("admin/users/create", new CreateUserPayload("Held", "Admin", email, IsActive: false));
        create.IsSuccessStatusCode.Should().BeTrue();
        var created = await create.ToResponseModel<UserDto>();
        InvitationsSentTo(email).Should().Be(0);

        var reactivate = await Client.PostAsync($"admin/users/{created.Id!.Value}/reactivate", null);
        reactivate.IsSuccessStatusCode.Should().BeTrue();

        InvitationsSentTo(email).Should().Be(1);
    }

    [Fact]
    public async Task ShouldInviteAtOnce_WhenTheUserIsCreatedActive()
    {
        const string email = "active.on.create@invitation-hold.gov.uk";

        var created = await CreateOrganisationUserAsync(email, "active");

        InvitationsSentTo(email).Should().Be(1);
        var saved = await Context.Users.AsNoTracking().FirstAsync(x => x.Id == new UserId(created.Id!.Value));
        saved.InvitationHeldAt.Should().BeNull();
    }

    // A user cannot sign in until their organisation is active too, so reactivating them first keeps the hold
    // rather than sending them an invitation that lands on the "organisation suspended" page.
    [Fact]
    public async Task ShouldKeepTheInvitationHeld_WhenTheUserIsReactivatedBeforeTheirOrganisation()
    {
        const string email = "reactivated.first@invitation-hold.gov.uk";
        var created = await CreateOrganisationUserAsync(email, "deactivated", _deactivatedOrganisation);

        var reactivate = await Client.PostAsync(OrganisationUserRoute(created.Id!.Value, "reactivate", _deactivatedOrganisation), null);
        reactivate.IsSuccessStatusCode.Should().BeTrue();

        InvitationsSentTo(email).Should().Be(0);
        var saved = await Context.Users.AsNoTracking().FirstAsync(x => x.Id == new UserId(created.Id.Value));
        saved.Status.Should().Be(UserStatus.Active);
        saved.InvitationHeldAt.Should().NotBeNull();
    }

    // Reactivating the organisation sends the held invitation to a user already reactivated individually, and to
    // nobody still deactivated: it reactivates none of its users (BLUM-7667 AC4).
    [Fact]
    public async Task ShouldSendTheHeldInvitation_WhenTheOrganisationIsReactivatedAfterTheUser()
    {
        const string reactivatedEmail = "reactivated.before.org@invitation-hold.gov.uk";
        const string stillDeactivatedEmail = "still.deactivated@invitation-hold.gov.uk";
        var reactivated = await CreateOrganisationUserAsync(reactivatedEmail, "deactivated", _deactivatedOrganisation);
        var stillDeactivated = await CreateOrganisationUserAsync(stillDeactivatedEmail, "deactivated", _deactivatedOrganisation);
        (await Client.PostAsync(OrganisationUserRoute(reactivated.Id!.Value, "reactivate", _deactivatedOrganisation), null))
            .IsSuccessStatusCode.Should().BeTrue();

        var reinstate = await Client.PostAsync($"organisations/local-authorities/{_deactivatedOrganisation.Id.Value}/reinstate", null);
        reinstate.IsSuccessStatusCode.Should().BeTrue();

        InvitationsSentTo(reactivatedEmail).Should().Be(1);
        (await Context.Users.AsNoTracking().FirstAsync(x => x.Id == new UserId(reactivated.Id.Value)))
            .InvitationHeldAt.Should().BeNull();

        InvitationsSentTo(stillDeactivatedEmail).Should().Be(0);
        var untouched = await Context.Users.AsNoTracking().FirstAsync(x => x.Id == new UserId(stillDeactivated.Id!.Value));
        untouched.Status.Should().Be(UserStatus.Deactivated);
        untouched.InvitationHeldAt.Should().NotBeNull();

        // Their own reactivation, now the organisation is active, is what sends theirs.
        (await Client.PostAsync(OrganisationUserRoute(stillDeactivated.Id.Value, "reactivate", _deactivatedOrganisation), null))
            .IsSuccessStatusCode.Should().BeTrue();
        InvitationsSentTo(stillDeactivatedEmail).Should().Be(1);
        InvitationsSentTo(reactivatedEmail).Should().Be(1);
    }

    // Adding a user to a deactivated organisation is allowed, even as active, but they cannot sign in until it is
    // reactivated - so the server holds their invitation whatever status the client asked for.
    [Fact]
    public async Task ShouldHoldTheInvitation_WhenAnActiveUserIsAddedToADeactivatedOrganisation()
    {
        const string email = "added.active.to.deactivated@invitation-hold.gov.uk";

        var created = await CreateOrganisationUserAsync(email, "active", _deactivatedOrganisation);

        InvitationsSentTo(email).Should().Be(0);
        (await Context.Users.AsNoTracking().FirstAsync(x => x.Id == new UserId(created.Id!.Value)))
            .InvitationHeldAt.Should().NotBeNull();

        var reinstate = await Client.PostAsync($"organisations/local-authorities/{_deactivatedOrganisation.Id.Value}/reinstate", null);
        reinstate.IsSuccessStatusCode.Should().BeTrue();

        InvitationsSentTo(email).Should().Be(1);
    }

    // The hold is cleared only once the email has actually gone. A failed send keeps it - the reactivation still
    // succeeds - and saving the user again retries it, since a second reactivation would be refused as already active.
    [Fact]
    public async Task ShouldKeepTheHold_WhenTheHeldInvitationFailsToSend_AndRetryOnTheNextSave()
    {
        const string email = "send.fails@invitation-hold.gov.uk";
        var created = await CreateOrganisationUserAsync(email, "deactivated");
        InMemoryInvitationEmailService.FailFor(email);

        try
        {
            var reactivate = await Client.PostAsync(OrganisationUserRoute(created.Id!.Value, "reactivate"), null);
            reactivate.IsSuccessStatusCode.Should().BeTrue();

            var afterFailure = await Context.Users.AsNoTracking().FirstAsync(x => x.Id == new UserId(created.Id.Value));
            afterFailure.Status.Should().Be(UserStatus.Active);
            afterFailure.InvitationHeldAt.Should().NotBeNull();
        }
        finally
        {
            InMemoryInvitationEmailService.StopFailingFor(email);
        }

        var save = await Client.PutAsJsonAsync($"organisations/{_organisation.Id.Value}/users/edit", created with { Status = "active" });
        save.IsSuccessStatusCode.Should().BeTrue();

        InvitationsSentTo(email).Should().Be(1);
        (await Context.Users.AsNoTracking().FirstAsync(x => x.Id == new UserId(created.Id.Value)))
            .InvitationHeldAt.Should().BeNull();
    }

    private static int InvitationsSentTo(string email) => InMemoryInvitationEmailService.Sent.Count(s => s.Email == email);

    private string OrganisationUserRoute(Guid userId, string action, Organisation? organisation = null) =>
        $"organisations/{(organisation ?? _organisation).Id.Value}/users/{userId}/{action}";

    private async Task<UserDto> CreateOrganisationUserAsync(string email, string status, Organisation? organisation = null)
    {
        var response = await Client.PostAsJsonAsync(
            $"organisations/{(organisation ?? _organisation).Id.Value}/users/create",
            new UserDto(Id: null, FirstName: "Invited", LastName: "Contact", Email: email, Status: status, Role: "lead", OrganisationName: null));

        response.IsSuccessStatusCode.Should().BeTrue();
        return await response.ToResponseModel<UserDto>();
    }

    public async Task InitializeAsync()
    {
        await Context.Organisations.AddRangeAsync(_organisation, _deactivatedOrganisation);
        await Context.Users.AddAsync(AdminsData.MainAdmin);
        await SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        Context.OrganisationUsers.RemoveRange(Context.OrganisationUsers);
        await SaveChangesAsync();

        Context.Users.RemoveRange(Context.Users);
        Context.Organisations.RemoveRange(Context.Organisations);
        Context.AuditLogs.RemoveRange(Context.AuditLogs);
        await SaveChangesAsync();
    }
}