using System.Net;
using Domain.AuditLogs;
using Domain.Users;
using Domain.ValueObjects;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Tests.Common;
using Xunit;

namespace Api.Tests.Integration.DataReset;

/// <summary>
/// POST /internal/user-emails/normalise (BLUM-7869's one-off backfill), audited by BLUM-8747. Gated by the same
/// DataReset shared secret as /internal/test-data/reset — appsettings.Test.json enables it for the suite. A dry run
/// changes nothing and so writes no audit row; a real run writes one, carrying counts and user ids but never an email
/// address.
/// </summary>
public class NormaliseUserEmailsTests(IntegrationTestWebFactory factory) : BaseIntegrationTest(factory), IAsyncLifetime
{
    private const string Route = "internal/user-emails/normalise";
    private const string SecretHeader = "X-Data-Reset-Secret";
    private const string ConfiguredSecret = "integration-test-harness-reset-secret";

    // Mixed case, as a row written before BLUM-7869 normalised at write time would hold it. User.New lower-cases the
    // address, so SeedLegacyUserAsync writes this over it with raw SQL.
    private const string LegacyEmail = "Legacy.Casing@Example.GOV.uk";
    private const string NormalisedEmail = "legacy.casing@example.gov.uk";

    private readonly User _user = User.New(
        id: UserId.New(),
        name: new Name("Legacy", "Casing"),
        email: NormalisedEmail,
        subId: new SubId(Guid.NewGuid()),
        status: UserStatus.Active,
        role: UserRole.Lead,
        utcNow: DateTime.UtcNow);

    [Fact]
    public async Task ShouldWriteNoAuditRow_OnADryRun()
    {
        await SeedUserAsync(LegacyEmail);

        var response = await NormaliseAsync(dryRun: true);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await Context.AuditLogs.AsNoTracking().AnyAsync(a => a.Action == AuditAction.SystemUserEmailsNormalised))
            .Should().BeFalse();

        var user = await Context.Users.AsNoTracking().SingleAsync(u => u.Id == _user.Id);
        user.Email.Should().Be(LegacyEmail, "a dry run changes nothing");
    }

    // Every address already normalised, so the run corrects nothing — but it is still a real run, and is audited.
    [Fact]
    public async Task ShouldWriteOneAuditRow_WithNoEmailAddresses_OnARealRun()
    {
        await SeedUserAsync(NormalisedEmail);

        var response = await NormaliseAsync(dryRun: false);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var row = await SingleNormalisedRowAsync();
        row.TargetType.Should().Be(AuditTargetType.System);
        row.TargetId.Should().Be(Guid.Empty);
        row.ActorUserId.Value.Should().Be(Guid.Empty);
        row.OrganisationId.Should().BeNull();
        row.After.Should().NotBeNull();
        row.After.Should().NotContain("@", "no email address may be copied into the audit log");
    }

    // A real run that has an address to correct. NormaliseUserEmailsCommandHandler folds the Graph result with
    // `graphResult.Match(_ => (string?)null, error => error.Message)`; LanguageExt's Either.Match throws
    // ResultIsNullException when a branch returns null, so the success branch throws, the handler answers 500
    // ("An unknown error occurred while normalising user emails."), the local row is left un-normalised although
    // Entra was already updated, and the audit row is never reached. Pre-dates BLUM-8747 (the Match line is not part
    // of its diff). Un-skip once the handler folds to something non-null (e.g. Option<string> or IsLeft/IfLeft).
    [Fact]
    public async Task ShouldCorrectAMixedCaseAddress_AndAuditItByIdOnly()
    {
        await SeedUserAsync(LegacyEmail);

        var response = await NormaliseAsync(dryRun: false);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await Context.Users.AsNoTracking().SingleAsync(u => u.Id == _user.Id)).Email.Should().Be(NormalisedEmail);

        var row = await SingleNormalisedRowAsync();
        row.After.Should().Contain(_user.Id.Value.ToString(), "the corrected user is identified by id");
        row.After.Should().NotContain("@", "no email address, before or after, may be copied into the audit log");
        row.After!.ToLowerInvariant().Should().NotContain("legacy.casing");
    }

    private async Task SeedUserAsync(string storedEmail)
    {
        await Context.Users.AddAsync(_user);
        await SaveChangesAsync();
        await Context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE users SET email = {storedEmail} WHERE id = {_user.Id.Value}");
    }

    private async Task<AuditLog> SingleNormalisedRowAsync()
    {
        var rows = await Context.AuditLogs.AsNoTracking()
            .Where(a => a.Action == AuditAction.SystemUserEmailsNormalised)
            .ToListAsync();
        return rows.Should().ContainSingle().Subject;
    }

    private async Task<HttpResponseMessage> NormaliseAsync(bool dryRun)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{Route}?dryRun={(dryRun ? "true" : "false")}");
        request.Headers.Add(SecretHeader, ConfiguredSecret);
        return await Client.SendAsync(request);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        Context.AuditLogs.RemoveRange(Context.AuditLogs);
        Context.Users.RemoveRange(Context.Users.Where(u => u.Id == _user.Id));
        await SaveChangesAsync();
        AuthenticateAsAdmin();
    }
}