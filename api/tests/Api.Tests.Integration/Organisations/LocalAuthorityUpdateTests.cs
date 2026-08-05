using System.Net;
using System.Net.Http.Json;
using Api.Dtos;
using Domain.AuditLogs;
using Domain.Organisations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Tests.Common;
using Tests.Data;
using Xunit;

namespace Api.Tests.Integration.Organisations;

public class LocalAuthorityUpdateTests(IntegrationTestWebFactory factory)
    : BaseIntegrationTest(factory), IAsyncLifetime
{
    private readonly Organisation _target = OrganisationsData.SecondOrganisation;

    [Fact]
    public async Task ShouldUpdate_PermittedFields_AndAuditLogDiff()
    {
        var request = new UpdateLocalAuthorityRequestDto(
            Name: "Updated Name",
            OnsCode: _target.OnsCode,
            Region: "London",
            Type: "LA",
            ECode: "E2000002",
            OnboardingStatus: null,
            ConfirmOnsCodeChange: false);

        var response = await Client.PutAsJsonAsync($"organisations/local-authorities/{_target.Id.Value}", request);
        response.IsSuccessStatusCode.Should().BeTrue();

        var saved = await Context.Organisations.AsNoTracking().FirstAsync(x => x.Id == _target.Id);
        saved.Name.Should().Be("Updated Name");
        saved.ECode.Should().Be("E2000002");

        var audit = await Context.AuditLogs.AsNoTracking()
            .Where(x => x.TargetType == AuditTargetType.Organisation && x.TargetId == _target.Id.Value)
            .ToListAsync();
        audit.Should().ContainSingle(x => x.Action == AuditAction.OrganisationUpdated);
        audit[0].Before.Should().Contain(_target.Name);
        audit[0].After.Should().Contain("Updated Name");
    }

    [Fact]
    public async Task ShouldReject_OnboardingStatusChange()
    {
        var request = new UpdateLocalAuthorityRequestDto(
            Name: _target.Name,
            OnsCode: _target.OnsCode,
            Region: _target.Region.ToString(),
            Type: _target.Type.ToString(),
            ECode: "E1000001",
            OnboardingStatus: "active",
            ConfirmOnsCodeChange: false);

        var response = await Client.PutAsJsonAsync($"organisations/local-authorities/{_target.Id.Value}", request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ShouldRequire_ConfirmFlag_WhenChangingOnsCode()
    {
        var request = new UpdateLocalAuthorityRequestDto(
            Name: _target.Name,
            OnsCode: "E06000099",
            Region: _target.Region.ToString(),
            Type: _target.Type.ToString(),
            ECode: "E1000001",
            OnboardingStatus: null,
            ConfirmOnsCodeChange: false);

        var response = await Client.PutAsJsonAsync($"organisations/local-authorities/{_target.Id.Value}", request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ShouldAcceptOnsChange_WhenConfirmed()
    {
        var request = new UpdateLocalAuthorityRequestDto(
            Name: _target.Name,
            OnsCode: "E06000099",
            Region: _target.Region.ToString(),
            Type: _target.Type.ToString(),
            ECode: "E1000001",
            OnboardingStatus: null,
            ConfirmOnsCodeChange: true);

        var response = await Client.PutAsJsonAsync($"organisations/local-authorities/{_target.Id.Value}", request);
        response.IsSuccessStatusCode.Should().BeTrue();

        var saved = await Context.Organisations.AsNoTracking().FirstAsync(x => x.Id == _target.Id);
        saved.OnsCode.Should().Be("E06000099");
    }

    // BLUM-7051: a Local Authority's code can be changed but never cleared. ConfirmOnsCodeChange is
    // deliberately true here — the confirm flag acknowledges a change, it is not a licence to blank the
    // field, so the validator must reject the request before that gate is even considered.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ShouldReject_ClearingOnsCode_ForALocalAuthority(string? onsCode)
    {
        var request = new UpdateLocalAuthorityRequestDto(
            Name: _target.Name,
            OnsCode: onsCode,
            Region: _target.Region.ToString(),
            Type: _target.Type.ToString(),
            ECode: "E1000001",
            OnboardingStatus: null,
            ConfirmOnsCodeChange: true);

        var response = await Client.PutAsJsonAsync($"organisations/local-authorities/{_target.Id.Value}", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var saved = await Context.Organisations.AsNoTracking().FirstAsync(x => x.Id == _target.Id);
        saved.OnsCode.Should().Be(_target.OnsCode);
    }

    // BLUM-7050: the E-code is mandatory on edit as well as create, so an organisation cannot be edited into
    // a state the create path would have rejected. Organisations that predate the rule hold no E-code, so
    // their first edit has to supply one.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ShouldReject_ClearingTheECode(string? eCode)
    {
        var request = new UpdateLocalAuthorityRequestDto(
            Name: _target.Name,
            OnsCode: _target.OnsCode,
            Region: _target.Region.ToString(),
            Type: _target.Type.ToString(),
            ECode: eCode,
            OnboardingStatus: null,
            ConfirmOnsCodeChange: false);

        var response = await Client.PutAsJsonAsync($"organisations/local-authorities/{_target.Id.Value}", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var saved = await Context.Organisations.AsNoTracking().FirstAsync(x => x.Id == _target.Id);
        saved.ECode.Should().Be(_target.ECode);
    }

    // The point of the "TBC" backfill (BLUM-7050): an organisation that predates the rule must stay editable.
    // The sentinel is non-empty, so it satisfies the new NotEmpty rule and an admin can change an unrelated
    // field without being blocked by an E-code they have never seen. Enforcing the rule without the backfill
    // would have made every one of these organisations un-editable.
    [Fact]
    public async Task ShouldAllowEditing_AnOrganisationHoldingTheBackfillSentinel()
    {
        var legacy = Organisation.New(
            OrganisationId.New(),
            "Legacy Council",
            "E06000301",
            Region.London,
            OrgType.LocalAuthority,
            "TBC",
            OnboardingStatus.Active);
        await Context.Organisations.AddAsync(legacy);
        await SaveChangesAsync();

        // Region changes, E-code left exactly as the migration wrote it.
        var untouched = await Client.PutAsJsonAsync(
            $"organisations/local-authorities/{legacy.Id.Value}",
            new UpdateLocalAuthorityRequestDto(
                Name: "Legacy Council",
                OnsCode: "E06000301",
                Region: "North West",
                Type: "LA",
                ECode: "TBC",
                OnboardingStatus: null,
                ConfirmOnsCodeChange: false));

        untouched.IsSuccessStatusCode.Should().BeTrue();

        // And the sentinel can then be replaced with the real code.
        var corrected = await Client.PutAsJsonAsync(
            $"organisations/local-authorities/{legacy.Id.Value}",
            new UpdateLocalAuthorityRequestDto(
                Name: "Legacy Council",
                OnsCode: "E06000301",
                Region: "North West",
                Type: "LA",
                ECode: "E9000001",
                OnboardingStatus: null,
                ConfirmOnsCodeChange: false));

        corrected.IsSuccessStatusCode.Should().BeTrue();
        var saved = await Context.Organisations.AsNoTracking().FirstAsync(x => x.Id == legacy.Id);
        saved.ECode.Should().Be("E9000001");
    }

    // The schema half of the same rule: e_code is NOT NULL, so a null can no longer reach the column even if a
    // future write path bypasses both the validator and the aggregate guard.
    [Fact]
    public async Task ECodeColumn_ShouldBeNotNull()
    {
        var isNullable = await Context.Database
            .SqlQueryRaw<string>(
                """
                SELECT is_nullable AS "Value"
                FROM information_schema.columns
                WHERE table_name = 'organisations' AND column_name = 'e_code'
                """)
            .SingleAsync();

        isNullable.Should().Be("NO");
    }

    // BLUM-7050 review: the backfill predicate must treat every value C#'s string.IsNullOrWhiteSpace calls blank
    // as blank, or the database keeps a row the application considers to have no E-code. Single-argument btrim()
    // strips ASCII space ONLY, so tab/newline/CR/form-feed/vertical-tab/NBSP slipped through into the NOT NULL
    // column and — because the clean-up is driven by grepping for the literal 'TBC' — would never be corrected.
    //
    // The migration itself cannot be re-run here (it is recorded in __EFMigrationsHistory), so this exercises the
    // same predicate against the same table to pin the behaviour the migration relies on.
    [Theory]
    [InlineData("\t")]
    [InlineData("\n")]
    [InlineData("\r")]
    [InlineData("\f")]
    [InlineData("\v")]
    [InlineData(" ")]
    [InlineData(" \t\n  ")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BackfillPredicate_ShouldTreatEveryWhitespaceOnlyValue_AsBlank(string candidate)
    {
        var matches = await Context.Database
            .SqlQueryRaw<bool>(
                """
                SELECT btrim({0}, E' \t\n\r\f\x0B\u00A0') = '' AS "Value"
                """,
                candidate)
            .SingleAsync();

        matches.Should().BeTrue();
        // Cross-check that the two languages agree, which is the actual invariant.
        string.IsNullOrWhiteSpace(candidate).Should().BeTrue();
    }

    // "v" and "vvv" are the regression guard for the trim set: PostgreSQL has no \v escape, so writing the
    // vertical tab as E'\v' silently puts the letter 'v' in the set and rewrites these codes to 'TBC'.
    [Theory]
    [InlineData("E9000123")]
    [InlineData("  E9000123  ")]
    [InlineData("TBC")]
    [InlineData("v")]
    [InlineData("vvv")]
    public async Task BackfillPredicate_ShouldNotMatch_ARealECode(string candidate)
    {
        var matches = await Context.Database
            .SqlQueryRaw<bool>(
                """
                SELECT btrim({0}, E' \t\n\r\f\x0B\u00A0') = '' AS "Value"
                """,
                candidate)
            .SingleAsync();

        matches.Should().BeFalse();
        string.IsNullOrWhiteSpace(candidate).Should().BeFalse();
    }

    public async Task InitializeAsync()
    {
        await Context.Organisations.AddAsync(_target);
        await SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        Context.AuditLogs.RemoveRange(Context.AuditLogs);
        Context.OrgFundingAllocations.RemoveRange(Context.OrgFundingAllocations);
        Context.FundingWindows.RemoveRange(Context.FundingWindows);
        Context.Organisations.RemoveRange(Context.Organisations);
        await SaveChangesAsync();
    }
}