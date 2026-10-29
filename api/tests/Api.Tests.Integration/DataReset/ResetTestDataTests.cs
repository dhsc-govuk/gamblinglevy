using System.Net;
using Application.Common.Settings;
using Domain.AuditLogs;
using Domain.Categories;
using Domain.Users;
using Domain.ValueObjects;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tests.Common;
using Tests.Data;
using Xunit;

namespace Api.Tests.Integration.DataReset;

/// <summary>
/// POST /internal/test-data/reset — the test-only full database reset (truncate every table except users,
/// delete non-admin users, then re-run ApplicationDbContextInitialiser.SeedDataAsync). DHSC admin accounts
/// are the one carve-out: they are real platform access, not test fixtures, so the reset preserves them
/// rather than forcing every admin to be re-provisioned. Deliberately unauthenticated: it is gated by
/// ApplicationSettings.DataReset (Enabled + a shared secret header) rather than [Authorize], since it exists
/// so an automated test harness can reset state before any user session is minted. appsettings.Test.json
/// fixes DataReset.Enabled = true and DataReset.Secret = "integration-test-harness-reset-secret" for the
/// whole suite; ShouldReturnNotFound_WhenDataResetIsDisabled flips it off for this class's own host only —
/// IntegrationTestWebFactory is an IClassFixture, so the ApplicationSettings instance belongs to this class
/// alone (see RateLimitingTests for the same pattern against a different settings singleton).
/// </summary>
public class ResetTestDataTests : BaseIntegrationTest, IAsyncLifetime
{
    private const string Route = "internal/test-data/reset";
    private const string SecretHeader = "X-Data-Reset-Secret";
    private const string ConfiguredSecret = "integration-test-harness-reset-secret";

    private readonly ApplicationSettings settings;

    public ResetTestDataTests(IntegrationTestWebFactory factory)
        : base(factory)
    {
        settings = factory.Services.GetRequiredService<ApplicationSettings>();
    }

    [Fact]
    public async Task ShouldTruncateAndReseed_ButPreserveAdminUsers_WhenSecretIsCorrect()
    {
        // Arrange — a pure user-data row (Organisations); a non-admin user (an LA Lead — wiped like every
        // other test fixture); and a second DHSC admin alongside the boot-seeded one, to prove admin users
        // are the one carve-out (real platform access, not test data) rather than surviving by accident of
        // the Users table never being empty in the first place.
        var nonAdminUser = User.New(
            id: UserId.New(),
            name: new Name("Org", "Lead"),
            email: "org.lead@example.com",
            subId: new SubId(Guid.NewGuid()),
            status: UserStatus.Active,
            role: UserRole.Lead,
            utcNow: DateTime.UtcNow);

        // An admin's TOTP enrolment and a live session, alongside a non-admin's — proving admin MFA data
        // survives (not just the admin's Users row) is the whole point of this fixture: user_mfas and
        // mfa_sessions were originally truncated unconditionally, which forced every surviving admin to
        // re-enrol their authenticator on every reset.
        var adminMfa = UserMfa.New(UserMfaId.New(), AdminsData.SecondAdmin.Id, "encrypted-admin-secret", DateTime.UtcNow);
        adminMfa.Enable(new List<string> { "hashed-recovery-code" }, DateTime.UtcNow);
        var adminSession = MfaSession.New(
            MfaSessionId.New(),
            AdminsData.SecondAdmin.Id,
            ipAddressHash: "admin-ip-hash",
            userAgentHash: "admin-agent-hash",
            ipAddress: "203.0.113.1",
            deviceLabel: "Admin's device",
            utcNow: DateTime.UtcNow);

        var nonAdminMfa = UserMfa.New(UserMfaId.New(), nonAdminUser.Id, "encrypted-non-admin-secret", DateTime.UtcNow);
        var nonAdminSession = MfaSession.New(
            MfaSessionId.New(),
            nonAdminUser.Id,
            ipAddressHash: "non-admin-ip-hash",
            userAgentHash: "non-admin-agent-hash",
            ipAddress: "203.0.113.2",
            deviceLabel: "Non-admin's device",
            utcNow: DateTime.UtcNow);

        await Context.Organisations.AddAsync(OrganisationsData.MainOrganisation);
        await Context.Users.AddAsync(AdminsData.SecondAdmin);
        await Context.Users.AddAsync(nonAdminUser);
        await Context.UserMfas.AddRangeAsync(adminMfa, nonAdminMfa);
        await Context.MfaSessions.AddRangeAsync(adminSession, nonAdminSession);
        await SaveChangesAsync();

        var adminIdsBefore = await Context.Users.AsNoTracking()
            .Where(u => u.Role == UserRole.Admin)
            .Select(u => u.Id)
            .ToListAsync();

        // Not asserting a specific count: whether the boot-time default admin still exists at this point
        // depends on test method execution order within the class (a prior test's teardown may have cleared
        // it) and is not what this test is about. What matters is that SecondAdmin — an admin this test adds
        // itself — is among the survivors before and after.
        adminIdsBefore.Should().Contain(AdminsData.SecondAdmin.Id);

        using var request = new HttpRequestMessage(HttpMethod.Post, Route);
        request.Headers.Add(SecretHeader, ConfiguredSecret);

        // Act
        var response = await Client.SendAsync(request);

        // Assert — response
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Assert — every user-owned row is gone
        (await Context.Organisations.AsNoTracking().AnyAsync()).Should().BeFalse();

        // Assert — admin users survive untouched; every non-admin user is gone. UserRole has no operator==
        // overload — only ValueObject.Equals — so the in-memory predicate below must call .Equals(...)
        // rather than use ==, which would silently fall back to reference equality.
        var users = await Context.Users.AsNoTracking().ToListAsync();
        users.Should().OnlyContain(u => u.Role.Equals(UserRole.Admin));
        users.Select(u => u.Id).Should().Contain(adminIdsBefore);
        users.Select(u => u.Id).Should().NotContain(nonAdminUser.Id);

        // Assert — the admin's MFA enrolment and session survive untouched; the non-admin's are gone
        var survivingMfaUserIds = await Context.UserMfas.AsNoTracking().Select(m => m.UserId).ToListAsync();
        survivingMfaUserIds.Should().Contain(AdminsData.SecondAdmin.Id);
        survivingMfaUserIds.Should().NotContain(nonAdminUser.Id);

        var survivingSessionUserIds = await Context.MfaSessions.AsNoTracking().Select(s => s.UserId).ToListAsync();
        survivingSessionUserIds.Should().Contain(AdminsData.SecondAdmin.Id);
        survivingSessionUserIds.Should().NotContain(nonAdminUser.Id);

        // Assert — reseeded back to the baseline, not merely left empty. Seven categories: the six
        // SeedActivityReferenceData writes, plus Joint partnership, which arrives from its own row-guarded seeder
        // (BLUM-7860) — so this count also proves the two seeders compose on a freshly truncated database.
        (await Context.Categories.AsNoTracking().CountAsync()).Should().Be(7);
        (await Context.Categories.AsNoTracking().CountAsync(c => c.Name == CategoryNames.JointPartnership)).Should().Be(1);
        (await Context.ActivityTasks.AsNoTracking().AnyAsync()).Should().BeTrue();
        (await Context.FormSections.AsNoTracking().AnyAsync()).Should().BeTrue();
        (await Context.FormModules.AsNoTracking().CountAsync()).Should().Be(3);
        (await Context.KeyResultDefinitions.AsNoTracking().AnyAsync()).Should().BeTrue();
        (await Context.KeyResultQuestions.AsNoTracking().AnyAsync()).Should().BeTrue();
    }

    // BLUM-8747: the reset truncates audit_logs and then records itself, so the fresh log holds exactly that one row.
    // The endpoint is gated by a shared secret, not a signed-in user, hence the empty actor.
    [Fact]
    public async Task ShouldLeaveExactlyOneTestDataResetAuditRow_WithAnEmptyActor()
    {
        await Context.AuditLogs.AddAsync(
            AuditLog.Create(AdminsData.MainAdmin.Id, AuditTargetType.User, Guid.NewGuid(), AuditAction.UserCreated, null, null));
        await SaveChangesAsync();

        using var request = new HttpRequestMessage(HttpMethod.Post, Route);
        request.Headers.Add(SecretHeader, ConfiguredSecret);

        var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var rows = await Context.AuditLogs.AsNoTracking().ToListAsync();
        var row = rows.Should().ContainSingle().Subject;
        row.Action.Should().Be(AuditAction.SystemTestDataReset);
        row.TargetType.Should().Be(AuditTargetType.System);
        row.TargetId.Should().Be(Guid.Empty);
        row.ActorUserId.Value.Should().Be(Guid.Empty);
        row.OrganisationId.Should().BeNull();
        row.After.Should().Contain("\"succeeded\": true", "jsonb stores it normalised");
    }

    [Theory]
    [InlineData("wrong-secret")]
    [InlineData("")]
    public async Task ShouldReturnNotFound_WhenSecretIsMissingOrWrong(string secret)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Route);
        if (!string.IsNullOrEmpty(secret))
        {
            request.Headers.Add(SecretHeader, secret);
        }

        var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ShouldReturnNotFound_WhenNoSecretHeaderIsSent()
    {
        var response = await Client.PostAsync(Route, content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ShouldReturnNotFound_WhenDataResetIsDisabled()
    {
        settings.DataReset.Enabled = false;

        using var request = new HttpRequestMessage(HttpMethod.Post, Route);
        request.Headers.Add(SecretHeader, ConfiguredSecret);

        var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        // Restore the class-wide default a prior test method may have flipped, or every method scheduled
        // after ShouldReturnNotFound_WhenDataResetIsDisabled on this host would see the endpoint disabled.
        settings.DataReset.Enabled = true;

        Context.AuditLogs.RemoveRange(Context.AuditLogs);
        Context.Organisations.RemoveRange(Context.Organisations);
        Context.MfaSessions.RemoveRange(Context.MfaSessions);
        Context.UserMfas.RemoveRange(Context.UserMfas);
        Context.Users.RemoveRange(Context.Users);
        await SaveChangesAsync();
    }
}