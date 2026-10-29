using System.Text.Json;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Application.Common.Settings;
using Domain.AuditLogs;
using Domain.Users;
using FluentAssertions;
using Infrastructure.AuditLogs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tests.Common;
using Xunit;

namespace Api.Tests.Integration.AuditLogs;

/// <summary>
/// BLUM-8750 — enforcing AuditSettings.RetentionDays. Driven through IAuditLogRetentionSweeper rather than the
/// hosted service that schedules it, as MfaSessionRetentionTests does and for the same reason; the two hosted
/// service tests at the end construct it by hand because IntegrationTestWebFactory unhooks it from the host.
/// </summary>
public class AuditLogRetentionTests : BaseIntegrationTest, IAsyncLifetime
{
    private const int RetentionDays = 365;

    private static readonly UserId Actor = new(Guid.Parse("b8750000-0000-0000-0000-000000000001"));

    private readonly IntegrationTestWebFactory _factory;
    private readonly ApplicationSettings _settings;

    public AuditLogRetentionTests(IntegrationTestWebFactory factory)
        : base(factory)
    {
        _factory = factory;
        _settings = factory.Services.GetRequiredService<ApplicationSettings>();
    }

    [Fact]
    public async Task ShouldDeleteNothing_WhenRetentionIsDisabled()
    {
        // The shipped default: no environment loses audit history until a period is agreed and set.
        _settings.Audit.RetentionDays = 0;
        var ancient = await SeedAsync(DateTime.UtcNow.AddYears(-10));

        var deleted = await SweepAsync();

        deleted.Should().Be(0);
        (await ExistsAsync(ancient)).Should().BeTrue();
        (await PurgeRowsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task ShouldDeleteRowsOlderThanTheRetentionPeriod_AndKeepNewerOnes()
    {
        var cutoff = DateTime.UtcNow.AddDays(-RetentionDays);
        var justExpired = await SeedAsync(cutoff.AddMinutes(-1));
        var justInside = await SeedAsync(cutoff.AddMinutes(1));
        var recent = await SeedAsync(DateTime.UtcNow.AddDays(-1));

        var deleted = await SweepAsync();

        deleted.Should().Be(1);
        (await ExistsAsync(justExpired)).Should().BeFalse();
        (await ExistsAsync(justInside)).Should().BeTrue();
        (await ExistsAsync(recent)).Should().BeTrue();
    }

    [Fact]
    public async Task ShouldDeleteEveryActionAlike_IncludingDeletionRecords()
    {
        // No exemptions: the deleted-users and deleted-organisations screens lose an entry once its row ages out.
        var userDeleted = await SeedAsync(DaysPastRetention(), AuditAction.UserDeleted);
        var organisationDeleted = await SeedAsync(DaysPastRetention(), AuditAction.OrganisationDeleted);

        var deleted = await SweepAsync();

        deleted.Should().Be(2);
        (await ExistsAsync(userDeleted)).Should().BeFalse();
        (await ExistsAsync(organisationDeleted)).Should().BeFalse();
    }

    [Fact]
    public async Task ShouldRecordThePurge_InOneSystemRow()
    {
        await SeedAsync(DaysPastRetention());
        await SeedAsync(DaysPastRetention());

        await SweepAsync();

        var purge = (await PurgeRowsAsync()).Should().ContainSingle().Subject;
        purge.ActorUserId.Should().Be(UserId.Empty());
        purge.TargetType.Should().Be(AuditTargetType.System);
        purge.TargetId.Should().Be(Guid.Empty);
        purge.Before.Should().BeNull();

        using var after = JsonDocument.Parse(purge.After!);
        after.RootElement.GetProperty("deleted").GetInt32().Should().Be(2);
        after.RootElement.GetProperty("retentionDays").GetInt32().Should().Be(RetentionDays);
    }

    [Fact]
    public async Task ShouldNotRecordAPurge_WhenNothingWasDeleted()
    {
        await SeedAsync(DateTime.UtcNow.AddDays(-1));

        var deleted = await SweepAsync();

        deleted.Should().Be(0);
        (await PurgeRowsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task ShouldClearABacklogAcrossSeveralBatches()
    {
        _settings.Audit.RetentionSweepBatchSize = 2;
        for (var i = 0; i < 5; i++)
        {
            await SeedAsync(DaysPastRetention());
        }

        var deleted = await SweepAsync();

        deleted.Should().Be(5);
        (await SeededRowCountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ShouldStopAfterTwentyBatches_AndLeaveTheRestForTheNextSweep()
    {
        // A cutoff misconfigured into the future must not empty the table in one run.
        _settings.Audit.RetentionSweepBatchSize = 1;
        for (var i = 0; i < 22; i++)
        {
            await SeedAsync(DaysPastRetention());
        }

        var deleted = await SweepAsync();

        deleted.Should().Be(20);
        (await SeededRowCountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task ShouldDeleteNoMoreThanTheBatchSize_InOneStatement()
    {
        for (var i = 0; i < 4; i++)
        {
            await SeedAsync(DaysPastRetention());
        }

        await using var scope = _factory.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();

        var result = await repository.DeleteOccurredBeforeAsync(DateTime.UtcNow.AddDays(-RetentionDays), batchSize: 3);

        result.Found.Should().Be(3);
        result.Deleted.Should().Be(3);
        (await SeededRowCountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ShouldSweepAsSoonAsTheHostStarts()
    {
        // Interval 0 leaves only the startup sweep, so no timer outlives the test.
        _settings.Audit.RetentionSweepIntervalHours = 0;
        var stale = await SeedAsync(DaysPastRetention());

        await RunHostedServiceUntilGoneAsync(stale);
    }

    [Fact]
    public async Task ShouldNotStopTheHost_WhenTheSweepIntervalIsUnusable()
    {
        // PeriodicTimer refuses a period beyond ~49.7 days; an hand-typed "8760" must be clamped, not thrown.
        _settings.Audit.RetentionSweepIntervalHours = 8760;
        var stale = await SeedAsync(DaysPastRetention());

        await RunHostedServiceUntilGoneAsync(stale);
    }

    private async Task RunHostedServiceUntilGoneAsync(AuditLogId stale)
    {
        var hostedService = new AuditLogRetentionHostedService(
            _factory.Services.GetRequiredService<IServiceScopeFactory>(),
            _settings,
            _factory.Services.GetRequiredService<ILogger<AuditLogRetentionHostedService>>());

        await hostedService.StartAsync(CancellationToken.None);

        try
        {
            // StartAsync does not wait for ExecuteAsync to finish, so poll rather than assert immediately.
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (await ExistsAsync(stale) && DateTime.UtcNow < deadline)
            {
                await Task.Delay(50);
            }

            (await ExistsAsync(stale)).Should().BeFalse("the hosted service sweeps once on startup");
            hostedService.ExecuteTask!.IsFaulted.Should().BeFalse();
        }
        finally
        {
            await hostedService.StopAsync(CancellationToken.None);
            hostedService.Dispose();
        }
    }

    private async Task<int> SweepAsync()
    {
        // Its own scope, exactly as the hosted service does it.
        await using var scope = _factory.Services.CreateAsyncScope();
        var sweeper = scope.ServiceProvider.GetRequiredService<IAuditLogRetentionSweeper>();

        return await sweeper.SweepAsync();
    }

    private static DateTime DaysPastRetention() => DateTime.UtcNow.AddDays(-RetentionDays - 1);

    /// <summary>A row back-dated to <paramref name="occurredAt"/>, through the change tracker since OccurredAt has no setter.</summary>
    private async Task<AuditLogId> SeedAsync(DateTime occurredAt, string action = AuditAction.UserUpdated)
    {
        var log = AuditLog.Create(Actor, AuditTargetType.User, Guid.NewGuid(), action, before: null, after: null);
        var entry = await Context.AuditLogs.AddAsync(log);
        entry.Property(x => x.OccurredAt).CurrentValue = occurredAt;
        await SaveChangesAsync();

        return log.Id;
    }

    private async Task<bool> ExistsAsync(AuditLogId id)
    {
        Context.ChangeTracker.Clear();
        return await Context.AuditLogs.AsNoTracking().AnyAsync(x => x.Id == id);
    }

    private async Task<int> SeededRowCountAsync()
    {
        Context.ChangeTracker.Clear();
        return await Context.AuditLogs.AsNoTracking().CountAsync(x => x.ActorUserId == Actor);
    }

    private async Task<List<AuditLog>> PurgeRowsAsync()
    {
        Context.ChangeTracker.Clear();
        return await Context.AuditLogs.AsNoTracking()
            .Where(x => x.Action == AuditAction.SystemAuditLogsPurged)
            .ToListAsync();
    }

    private void ResetSettings()
    {
        _settings.Audit.RetentionDays = RetentionDays;
        _settings.Audit.RetentionSweepBatchSize = 1000;
        _settings.Audit.RetentionSweepIntervalHours = 24;
    }

    public async Task InitializeAsync()
    {
        ResetSettings();
        await ClearAsync();
    }

    public async Task DisposeAsync()
    {
        // Back to the shipped default, so nothing else this class's host runs can sweep.
        _settings.Audit.RetentionDays = 0;
        _settings.Audit.RetentionSweepBatchSize = 1000;
        _settings.Audit.RetentionSweepIntervalHours = 24;

        await ClearAsync();
        AuthenticateAsAdmin();
    }

    private async Task ClearAsync()
    {
        Context.AuditLogs.RemoveRange(Context.AuditLogs);
        await SaveChangesAsync();
    }
}