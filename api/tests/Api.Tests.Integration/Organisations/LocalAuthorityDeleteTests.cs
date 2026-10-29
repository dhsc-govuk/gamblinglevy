using System.Net;
using System.Text.Json;
using Domain.AuditLogs;
using Domain.MaturityAssessments;
using Domain.Organisations;
using Domain.OrganisationUsers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Tests.Common;
using Tests.Data;
using Xunit;

namespace Api.Tests.Integration.Organisations;

public class LocalAuthorityDeleteTests(IntegrationTestWebFactory factory)
    : BaseIntegrationTest(factory), IAsyncLifetime
{
    private readonly Organisation _invited = Organisation.New(
        OrganisationId.New(),
        "Invited Council",
        "E06000050",
        Region.London,
        OrgType.LocalAuthority,
        "E1000001",
        OnboardingStatus.Invited,
        DateTime.UtcNow);

    private readonly Organisation _active = Organisation.New(
        OrganisationId.New(),
        "Active Council",
        "E06000051",
        Region.London,
        OrgType.LocalAuthority,
        "E1000001",
        OnboardingStatus.Active,
        DateTime.UtcNow);

    private readonly Organisation _suspended = Organisation.New(
        OrganisationId.New(),
        "Suspended Council",
        "E06000052",
        Region.London,
        OrgType.LocalAuthority,
        "E1000001",
        OnboardingStatus.Suspended,
        DateTime.UtcNow);

    private readonly Organisation _invitedWithUsers = Organisation.New(
        OrganisationId.New(),
        "Council With Users",
        "E06000053",
        Region.London,
        OrgType.LocalAuthority,
        "E1000001",
        OnboardingStatus.Invited,
        DateTime.UtcNow);

    private OrganisationUser _activeOrgUser = null!;

    [Fact]
    public async Task ShouldDelete_InvitedLA_AndKeepAuditLog()
    {
        var response = await Client.DeleteAsync($"organisations/local-authorities/{_invited.Id.Value}");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var remaining = await Context.Organisations.AsNoTracking().AnyAsync(x => x.Id == _invited.Id);
        remaining.Should().BeFalse();

        var audit = await Context.AuditLogs.AsNoTracking()
            .Where(x => x.TargetType == AuditTargetType.Organisation && x.TargetId == _invited.Id.Value)
            .ToListAsync();
        audit.Should().ContainSingle(x => x.Action == AuditAction.OrganisationDeleted);
    }

    [Fact]
    public async Task ShouldDelete_SuspendedLA()
    {
        var response = await Client.DeleteAsync($"organisations/local-authorities/{_suspended.Id.Value}");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task ShouldRejectDelete_OfActiveLA()
    {
        var response = await Client.DeleteAsync($"organisations/local-authorities/{_active.Id.Value}");
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    // A started maturity assessment is a record like a quarterly submission, and its rows carry no FK to the
    // organisation — the guard has to ask, or the delete orphans them silently (BLUM-7855 code review).
    [Fact]
    public async Task ShouldReject_WhenAMaturityAssessmentExists()
    {
        var assessment = MaturityAssessment.Start(MaturityAssessmentId.New(), Domain.Collections.CollectionId.New(), _invited.Id, ["la_capacity"], AdminsData.MainAdmin.Id, DateTime.UtcNow);
        await Context.MaturityAssessments.AddAsync(assessment);
        await SaveChangesAsync();

        var response = await Client.DeleteAsync($"organisations/local-authorities/{_invited.Id.Value}");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync()).Should().ContainEquivalentOf("submission");
        (await Context.Organisations.AsNoTracking().AnyAsync(x => x.Id == _invited.Id)).Should().BeTrue();
        (await Context.MaturityAssessments.AsNoTracking().AnyAsync(x => x.Id == assessment.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task ShouldReject_WhenActiveUsersExist_AndNoAcknowledgement()
    {
        var response = await Client.DeleteAsync($"organisations/local-authorities/{_invitedWithUsers.Id.Value}");
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("error").GetString().Should().Be("ORGANISATION_HAS_ACTIVE_USERS");
        var users = json.RootElement.GetProperty("activeUsers").EnumerateArray().ToList();
        users.Should().ContainSingle();
        users[0].GetProperty("email").GetString().Should().Be(_activeOrgUser.User!.Email);
        users[0].GetProperty("firstName").GetString().Should().Be(_activeOrgUser.User!.Name.FirstName);

        var remaining = await Context.Organisations.AsNoTracking().AnyAsync(x => x.Id == _invitedWithUsers.Id);
        remaining.Should().BeTrue();
    }

    [Fact]
    public async Task ShouldDelete_WhenActiveUsersExist_AndAcknowledged_AndRemoveOrganisationUserButKeepUser()
    {
        var userId = _activeOrgUser.UserId;

        var response = await Client.DeleteAsync(
            $"organisations/local-authorities/{_invitedWithUsers.Id.Value}?acknowledgeActiveUsers=true");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var orgRemaining = await Context.Organisations.AsNoTracking().AnyAsync(x => x.Id == _invitedWithUsers.Id);
        orgRemaining.Should().BeFalse();

        var orgUserRemaining = await Context.OrganisationUsers.AsNoTracking()
            .AnyAsync(x => x.OrganisationId == _invitedWithUsers.Id);
        orgUserRemaining.Should().BeFalse();

        var userRemaining = await Context.Users.AsNoTracking().AnyAsync(x => x.Id == userId);
        userRemaining.Should().BeTrue();

        var audit = await Context.AuditLogs.AsNoTracking()
            .Where(x => x.TargetType == AuditTargetType.Organisation && x.TargetId == _invitedWithUsers.Id.Value)
            .ToListAsync();
        audit.Should().ContainSingle(x => x.Action == AuditAction.OrganisationDeleted);
    }

    public async Task InitializeAsync()
    {
        _activeOrgUser = OrganisationUsersData.MainOrganisationUser(_invitedWithUsers.Id);

        await Context.Organisations.AddRangeAsync(_invited, _active, _suspended, _invitedWithUsers);
        await Context.OrganisationUsers.AddAsync(_activeOrgUser);
        await SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        Context.MaturityAssessments.RemoveRange(Context.MaturityAssessments);
        Context.AuditLogs.RemoveRange(Context.AuditLogs);
        Context.OrgFundingAllocations.RemoveRange(Context.OrgFundingAllocations);
        Context.FundingWindows.RemoveRange(Context.FundingWindows);
        Context.OrganisationUsers.RemoveRange(Context.OrganisationUsers);
        Context.Users.RemoveRange(Context.Users);
        Context.Organisations.RemoveRange(Context.Organisations);
        await SaveChangesAsync();
    }
}