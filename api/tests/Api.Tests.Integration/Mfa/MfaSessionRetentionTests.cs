using System.Security.Cryptography;
using System.Text;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Application.Common.Settings;
using Domain.AuditLogs;
using Domain.Users;
using FluentAssertions;
using Infrastructure.Mfa;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tests.Common;
using Tests.Data;
using Xunit;

namespace Api.Tests.Integration.Mfa;

/// <summary>
/// BLUM-7241 — enforcing MfaSettings.SessionRetentionDays. Since this ticket a session record carries an IP
/// address and a device description, so a record kept forever is personal data kept forever, and the service
/// publishes a retention period it has to actually honour.
///
/// Driven through IMfaSessionRetentionSweeper rather than through the hosted service that schedules it: the
/// scheduling is a PeriodicTimer and a startup call, and a test that waited on either would be slow and
/// flaky for no extra coverage. IntegrationTestWebFactory unhooks the hosted service for the whole suite
/// (see RemoveBackgroundServices) so a background sweep cannot delete rows out from under any other test.
///
/// No MFA gate needed here — the sweep never goes near the middleware.
/// </summary>
public class MfaSessionRetentionTests : BaseIntegrationTest, IAsyncLifetime
{
    private readonly IntegrationTestWebFactory _factory;
    private readonly ApplicationSettings _settings;

    public MfaSessionRetentionTests(IntegrationTestWebFactory factory)
        : base(factory)
    {
        _factory = factory;
        _settings = factory.Services.GetRequiredService<ApplicationSettings>();
    }

    [Fact]
    public async Task ShouldDeleteLongEndedSessions_ForEveryUser_IncludingOnesWhoNeverSignInAgain()
    {
        // The finding this exists for. Retention used to be swept on a user's own next sign-in, so the people
        // who had left the organisation — whose data has the least justification for being kept — were the
        // only ones never swept at all.
        var departedUsersSession = await SeedEndedSessionAsync(AdminsData.SecondAdmin, DaysPastRetention());
        var quietUsersSession = await SeedEndedSessionAsync(AdminsData.MainAdmin, DaysPastRetention());

        var deleted = await SweepAsync();

        deleted.Should().Be(2);
        (await SessionExistsAsync(departedUsersSession)).Should().BeFalse();
        (await SessionExistsAsync(quietUsersSession)).Should().BeFalse();
    }

    [Fact]
    public async Task ShouldNotDeleteRecentlyRevokedSessions()
    {
        // Why the retention window has to sit well beyond SessionExpiryHours. This row is what lets
        // MfaRequiredMiddleware keep answering SESSION_REVOKED; delete it and the revoked browser is told
        // MFA_REQUIRED instead, sent to re-enter a code, and a correct code would undo the revocation.
        var justRevoked = await SeedEndedSessionAsync(AdminsData.MainAdmin, endedAt: DateTime.UtcNow.AddMinutes(-1));
        await RevokeDirectlyAsync(justRevoked);

        var deleted = await SweepAsync();

        deleted.Should().Be(0);
        (await SessionExistsAsync(justRevoked)).Should().BeTrue();
    }

    [Fact]
    public async Task ShouldNotDeleteLiveSessions()
    {
        var live = await SeedEndedSessionAsync(AdminsData.MainAdmin, endedAt: DateTime.UtcNow.AddHours(8));

        var deleted = await SweepAsync();

        deleted.Should().Be(0);
        (await SessionExistsAsync(live)).Should().BeTrue();
    }

    [Fact]
    public async Task ShouldLeaveTheAuditTrailBehind_WhenARecordIsDeleted()
    {
        // audit_logs.target_id is a plain Guid with no foreign key to mfa_sessions, precisely so that an
        // investigation outlives the record it is about.
        var stale = await SeedEndedSessionAsync(AdminsData.MainAdmin, DaysPastRetention());

        await SweepAsync();

        (await SessionExistsAsync(stale)).Should().BeFalse();

        Context.ChangeTracker.Clear();
        var audit = await Context.AuditLogs.AsNoTracking()
            .Where(x => x.TargetType == AuditTargetType.MfaSession && x.TargetId == stale)
            .ToListAsync();

        audit.Should().ContainSingle(x => x.Action == AuditAction.MfaSessionCreated);
    }

    [Fact]
    public async Task ShouldClearABacklogAcrossSeveralBatches()
    {
        // The first run after a deployment may face a long backlog. It has to be cleared, but through several
        // bounded statements rather than one that holds locks on a shared table for as long as it takes.
        _settings.Mfa.SessionRetentionSweepBatchSize = 2;

        for (var i = 0; i < 5; i++)
        {
            await SeedEndedSessionAsync(AdminsData.MainAdmin, DaysPastRetention());
        }

        var deleted = await SweepAsync();

        deleted.Should().Be(5);
        Context.ChangeTracker.Clear();
        (await Context.MfaSessions.AsNoTracking().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task ShouldDeleteNoMoreThanTheBatchSize_InOneStatement()
    {
        for (var i = 0; i < 4; i++)
        {
            await SeedEndedSessionAsync(AdminsData.MainAdmin, DaysPastRetention());
        }

        using var scope = _factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IMfaSessionRepository>();

        var result = await repository.DeleteEndedSessionsAsync(DateTime.UtcNow.AddDays(-_settings.Mfa.SessionRetentionDays), batchSize: 2);

        // Found is what the sweep loops on; Deleted is what actually went. They match here because nothing
        // else is sweeping, but they are reported separately precisely because a concurrent sweep can make
        // Deleted come back short while the backlog is untouched.
        result.Found.Should().Be(2);
        result.Deleted.Should().Be(2);
        Context.ChangeTracker.Clear();
        (await Context.MfaSessions.AsNoTracking().CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task ShouldNotStopTheHost_WhenTheSweepIntervalIsUnusable()
    {
        // PeriodicTimer refuses a period beyond ~49.7 days, and this value is typed by hand into an App
        // Service setting. "8760" for once a year is a reasonable thing to write; unclamped it threw out of
        // ExecuteAsync and, with BackgroundServiceExceptionBehavior.StopHost, crash-looped the whole API.
        _settings.Mfa.SessionRetentionSweepIntervalHours = 8760;

        var stale = await SeedEndedSessionAsync(AdminsData.MainAdmin, DaysPastRetention());

        var hostedService = new MfaSessionRetentionHostedService(
            _factory.Services.GetRequiredService<IServiceScopeFactory>(),
            _settings,
            _factory.Services.GetRequiredService<ILogger<MfaSessionRetentionHostedService>>());

        await hostedService.StartAsync(CancellationToken.None);

        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (await SessionExistsAsync(stale) && DateTime.UtcNow < deadline)
            {
                await Task.Delay(50);
            }

            // The startup sweep still ran, and ExecuteAsync did not fault.
            (await SessionExistsAsync(stale)).Should().BeFalse();
            hostedService.ExecuteTask!.IsFaulted.Should().BeFalse();
        }
        finally
        {
            await hostedService.StopAsync(CancellationToken.None);
            hostedService.Dispose();
        }
    }

    [Fact]
    public async Task ShouldDeleteNothing_WhenRetentionIsDisabled()
    {
        _settings.Mfa.SessionRetentionDays = 0;

        var ancient = await SeedEndedSessionAsync(AdminsData.MainAdmin, DateTime.UtcNow.AddYears(-2));

        var deleted = await SweepAsync();

        deleted.Should().Be(0);
        (await SessionExistsAsync(ancient)).Should().BeTrue();
    }

    [Fact]
    public async Task ShouldSweepAsSoonAsTheHostStarts()
    {
        // Not a nicety — it is what makes retention work at all today. The App Service runs with
        // always_on = false, so the site unloads after ~20 minutes idle and a purely timer-driven job would
        // stop; sweeping on start means every cold start sweeps, and a cold start is what happens the next
        // time anybody uses the service. Interval 0 leaves only that startup sweep, so no timer outlives the
        // test. The service is constructed here rather than resolved because IntegrationTestWebFactory
        // deliberately unhooks it from the test host.
        _settings.Mfa.SessionRetentionSweepIntervalHours = 0;

        var stale = await SeedEndedSessionAsync(AdminsData.MainAdmin, DaysPastRetention());

        var hostedService = new MfaSessionRetentionHostedService(
            _factory.Services.GetRequiredService<IServiceScopeFactory>(),
            _settings,
            _factory.Services.GetRequiredService<ILogger<MfaSessionRetentionHostedService>>());

        await hostedService.StartAsync(CancellationToken.None);

        try
        {
            // StartAsync does not wait for ExecuteAsync to finish, so poll rather than assert immediately.
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (await SessionExistsAsync(stale) && DateTime.UtcNow < deadline)
            {
                await Task.Delay(50);
            }

            (await SessionExistsAsync(stale)).Should().BeFalse("the hosted service sweeps once on startup");
        }
        finally
        {
            await hostedService.StopAsync(CancellationToken.None);
            hostedService.Dispose();
        }
    }

    private async Task<int> SweepAsync()
    {
        // Its own scope, exactly as the hosted service does it — the sweeper and its DbContext are scoped.
        using var scope = _factory.Services.CreateScope();
        var sweeper = scope.ServiceProvider.GetRequiredService<IMfaSessionRetentionSweeper>();

        return await sweeper.SweepAsync();
    }

    private DateTime DaysPastRetention() => DateTime.UtcNow.AddDays(-_settings.Mfa.SessionRetentionDays - 1);

    /// <summary>
    /// Seeds a session whose absolute expiry is <paramref name="endedAt"/>, plus the audit entry the real
    /// CreateSessionAsync would have written — so a test can tell "the record went" apart from "its history
    /// went with it". ExpiresAt is set through the database because MfaSession deliberately exposes no setter.
    /// </summary>
    private async Task<Guid> SeedEndedSessionAsync(User user, DateTime endedAt)
    {
        if (!await Context.Users.AsNoTracking().AnyAsync(x => x.Id == user.Id))
        {
            await Context.Users.AddAsync(user);
            await SaveChangesAsync();
        }

        var session = MfaSession.New(
            MfaSessionId.New(),
            user.Id,
            ipAddressHash: HashForBinding("203.0.113.10"),
            userAgentHash: HashForBinding("IntegrationTest/1.0"),
            ipAddress: "203.0.113.10",
            deviceLabel: "Chrome on Windows",
            utcNow: DateTime.UtcNow);

        await Context.MfaSessions.AddAsync(session);
        await Context.AuditLogs.AddAsync(
            AuditLog.Create(user.Id, AuditTargetType.MfaSession, session.Id.Value, AuditAction.MfaSessionCreated, before: null, after: null));
        await SaveChangesAsync();

        await Context.MfaSessions
            .Where(x => x.Id == session.Id)
            .ExecuteUpdateAsync(x => x.SetProperty(s => s.ExpiresAt, endedAt));

        Context.ChangeTracker.Clear();

        return session.Id.Value;
    }

    private async Task RevokeDirectlyAsync(Guid sessionId)
    {
        await Context.MfaSessions
            .Where(x => x.Id == new MfaSessionId(sessionId))
            .ExecuteUpdateAsync(x => x.SetProperty(s => s.IsRevoked, true));

        Context.ChangeTracker.Clear();
    }

    private async Task<bool> SessionExistsAsync(Guid sessionId)
    {
        // The sweep ran in its own scope; this Context is long-lived and still tracks what it read before.
        Context.ChangeTracker.Clear();

        return await Context.MfaSessions
            .AsNoTracking()
            .AnyAsync(x => x.Id == new MfaSessionId(sessionId));
    }

    private static string HashForBinding(string input) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(input)));

    public async Task InitializeAsync()
    {
        _settings.Mfa.SessionRetentionDays = 30;
        _settings.Mfa.SessionRetentionSweepBatchSize = 500;
        _settings.Mfa.SessionRetentionSweepIntervalHours = 6;

        await ClearAsync();
    }

    public async Task DisposeAsync()
    {
        _settings.Mfa.SessionRetentionDays = 30;
        _settings.Mfa.SessionRetentionSweepBatchSize = 500;
        _settings.Mfa.SessionRetentionSweepIntervalHours = 6;

        await ClearAsync();

        AuthenticateAsAdmin();
    }

    private async Task ClearAsync()
    {
        Context.MfaSessions.RemoveRange(Context.MfaSessions);
        Context.UserMfas.RemoveRange(Context.UserMfas);
        Context.AuditLogs.RemoveRange(Context.AuditLogs);
        Context.OrganisationUsers.RemoveRange(Context.OrganisationUsers);
        Context.Users.RemoveRange(Context.Users);
        Context.Organisations.RemoveRange(Context.Organisations);
        await SaveChangesAsync();
    }
}