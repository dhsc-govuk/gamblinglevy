using Application.Common.Interfaces;
using Domain.AuditLogs;
using Domain.Organisations;
using Domain.Users;
using FluentAssertions;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Infrastructure.Persistence.Seed.MaturityBaseline;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tests.Common;
using Xunit;

namespace Api.Tests.Integration.AuditLogs;

/// <summary>
/// BLUM-8753 — the audit timestamp is stamped as the row is written, from the injected clock, rather than
/// supplied by each of the callers. Two writers do that: AuditLogRepository.AppendAsync, and
/// MaturityBaselineSeeder, which adds its row straight to the context because it cannot use the repository.
///
/// The clock is pinned to a past instant on purpose. occurred_at also carries a column default of the
/// database's now(), so a writer that stopped stamping would still produce a recent, plausible timestamp —
/// only an instant the database could not have produced tells the two apart.
/// </summary>
public class AuditLogAppendTimestampTests(IntegrationTestWebFactory factory)
    : BaseIntegrationTest(factory), IAsyncLifetime
{
    private static readonly DateTime Pinned = new(2020, 2, 3, 4, 5, 6, DateTimeKind.Utc);

    // A local authority the 2025 survey has a row for, so the seeder has something to import and audit.
    private static readonly Organisation SurveyedAuthority = Organisation.New(
        OrganisationId.New(), "Peterborough City Council", "E06000031", Region.EastOfEngland, OrgType.LocalAuthority, "E1000931", OnboardingStatus.Active, DateTime.UtcNow);

    [Fact]
    public async Task ShouldStampOccurredAtFromTheInjectedClock_WhenAppending()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var repository = new AuditLogRepository(context, new PinnedClock(), NullLogger<AuditLogRepository>.Instance);

        var entry = NewEntry();
        entry.OccurredAt.Should().Be(default, "the factory neither takes nor reads the time");

        await repository.AppendAsync(entry);

        var saved = await Context.AuditLogs.AsNoTracking().SingleAsync(x => x.Id == entry.Id);
        saved.OccurredAt.Should().Be(Pinned);
    }

    [Fact]
    public async Task ShouldStampTheBaselineImportRowFromTheInjectedClock_ThoughItBypassesTheRepository()
    {
        await Context.Organisations.AddAsync(SurveyedAuthority);
        await SaveChangesAsync();

        await using var scope = factory.Services.CreateAsyncScope();
        var seeder = new MaturityBaselineSeeder(
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
            scope.ServiceProvider.GetRequiredService<ILogger<MaturityBaselineSeeder>>(),
            new PinnedClock());

        await seeder.SeedAsync();

        var audit = await Context.AuditLogs.AsNoTracking().SingleAsync(x => x.Action == AuditAction.MaturityBaselineImported);
        audit.OccurredAt.Should().Be(Pinned);
    }

    [Fact]
    public async Task ShouldFallBackToTheColumnDefault_WhenARowBypassesTheRepository()
    {
        // The documented backstop: a row added straight to the context is left unstamped, and the column
        // default fills it in. Without that default it would be written as year 1 and sort before everything.
        var entry = NewEntry();

        await Context.AuditLogs.AddAsync(entry);
        await SaveChangesAsync();

        // Judged against the database's own clock, the one the default reads. Comparing with this process's
        // clock would fail on nothing more than drift between the two machines.
        var databaseNow = await Context.Database
            .SqlQuery<DateTime>($"SELECT timezone('utc', now()) AS \"Value\"")
            .SingleAsync();

        var saved = await Context.AuditLogs.AsNoTracking().SingleAsync(x => x.Id == entry.Id);
        saved.OccurredAt.Should().BeCloseTo(databaseNow, TimeSpan.FromMinutes(1));
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        Context.AuditLogs.RemoveRange(Context.AuditLogs);
        Context.MaturityBaselineAnswers.RemoveRange(Context.MaturityBaselineAnswers);
        Context.Organisations.RemoveRange(Context.Organisations.Where(x => x.Id == SurveyedAuthority.Id));
        await SaveChangesAsync();
    }

    private static AuditLog NewEntry() =>
        AuditLog.Create(UserId.New(), AuditTargetType.MfaSession, Guid.NewGuid(), AuditAction.MfaSessionIpChanged, before: null, after: null);

    private sealed class PinnedClock : IDateTimeProvider
    {
        public DateTime GetUtcNow() => Pinned;
    }
}