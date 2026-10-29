using Domain.AuditLogs;
using Domain.MaturityAssessments;
using Domain.Organisations;
using FluentAssertions;
using Infrastructure.Persistence.Seed.MaturityBaseline;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tests.Common;
using Xunit;

namespace Api.Tests.Integration.MaturityAssessments;

// The boot-time import of the 2025 survey (BLUM-7857): matched local authorities get their baseline rows once,
// VCSEs and unmatched councils get nothing, a re-run adds nothing, and the whole run leaves one audit row.
public class MaturityBaselineSeederTests(IntegrationTestWebFactory factory) : BaseIntegrationTest(factory), IAsyncLifetime
{
    private static readonly Organisation Peterborough = Organisation.New(
        OrganisationId.New(), "Peterborough City Council", "E06000031", Region.EastOfEngland, OrgType.LocalAuthority, "E1000931", OnboardingStatus.Active, DateTime.UtcNow);

    // The ONS spelling, to prove the matcher reconciles it with the survey's "Bristol City Council".
    private static readonly Organisation Bristol = Organisation.New(
        OrganisationId.New(), "Bristol, City of", "E06000023", Region.SouthWest, OrgType.LocalAuthority, "E1000923", OnboardingStatus.Active, DateTime.UtcNow);

    private static readonly Organisation Vcse = Organisation.New(
        OrganisationId.New(), "Luton Borough Council", null, Region.EastOfEngland, OrgType.Vcse, "E1000999", OnboardingStatus.Active, DateTime.UtcNow);

    private readonly IntegrationTestWebFactory _factory = factory;

    [Fact]
    public async Task Seed_ImportsMatchedLocalAuthoritiesOnce_AndAuditsTheRun()
    {
        await SeedAsync();

        var peterborough = await RowsForAsync(Peterborough);
        var bristol = await RowsForAsync(Bristol);
        peterborough.Should().HaveCountGreaterThan(20);
        bristol.Should().HaveCountGreaterThan(20);
        peterborough.Should().OnlyContain(r => r.SurveyYear == MaturityBaselineSeeder.SurveyYear);
        peterborough.Should().Contain(r => r.QuestionCode == "MA_DESIGNATED_LEAD" && r.Value == "yes");
        (await RowsForAsync(Vcse)).Should().BeEmpty("the survey only ever describes local authorities");

        var audits = await Context.AuditLogs.AsNoTracking()
            .Where(a => a.Action == AuditAction.MaturityBaselineImported)
            .ToListAsync();
        audits.Should().ContainSingle();
        audits[0].After.Should().Contain("Peterborough City Council").And.Contain("Bristol City Council").And.Contain("unmatched");

        var before = await Context.MaturityBaselineAnswers.AsNoTracking().CountAsync();
        await SeedAsync();

        (await Context.MaturityBaselineAnswers.AsNoTracking().CountAsync()).Should().Be(before, "the import converges");
        (await Context.AuditLogs.AsNoTracking().CountAsync(a => a.Action == AuditAction.MaturityBaselineImported)).Should().Be(1);
    }

    private async Task SeedAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<MaturityBaselineSeeder>().SeedAsync();
    }

    private async Task<List<MaturityBaselineAnswer>> RowsForAsync(Organisation organisation) =>
        await Context.MaturityBaselineAnswers.AsNoTracking().Where(a => a.OrganisationId == organisation.Id).ToListAsync();

    public async Task InitializeAsync()
    {
        AuthenticateAsAdmin();
        Context.MaturityBaselineAnswers.RemoveRange(Context.MaturityBaselineAnswers);
        Context.AuditLogs.RemoveRange(Context.AuditLogs);
        await SaveChangesAsync();

        await Context.Organisations.AddRangeAsync(Peterborough, Bristol, Vcse);
        await SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        Context.MaturityBaselineAnswers.RemoveRange(Context.MaturityBaselineAnswers);
        Context.AuditLogs.RemoveRange(Context.AuditLogs);
        Context.Organisations.RemoveRange(Context.Organisations.Where(o => o.Id == Peterborough.Id || o.Id == Bristol.Id || o.Id == Vcse.Id));
        await SaveChangesAsync();
        AuthenticateAsAdmin();
    }
}