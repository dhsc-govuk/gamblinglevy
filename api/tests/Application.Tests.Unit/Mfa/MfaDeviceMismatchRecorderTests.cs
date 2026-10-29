using System.Text.Json.Nodes;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Application.Common.Settings;
using Application.Tests.Unit.Security;
using Domain.AuditLogs;
using Domain.Users;
using FluentAssertions;
using Infrastructure.Mfa;
using Infrastructure.Security;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Application.Tests.Unit.Mfa;

/// <summary>
/// BLUM-8321 — the two contracts of the device-mismatch recorder that the integration suite cannot reach: the
/// suppressed tally only surfaces once the hourly window resets, which needs a clock the test can move, and
/// "never throws" needs an audit write that fails on demand.
/// </summary>
public class MfaDeviceMismatchRecorderTests
{
    [Fact]
    public async Task ShouldCarrySuppressedRefusalsOntoTheNextAdmittedRow()
    {
        var repository = new CapturingAuditLogRepository();
        var recorder = RecorderFor(repository, perUserCap: 1, out var clock);
        var mismatch = Mismatch();

        for (var i = 0; i < 4; i++)
        {
            await recorder.RecordAsync(mismatch);
        }

        repository.Entries.Should().ContainSingle("the cap admits one row an hour for this user");

        clock.UtcNow = clock.UtcNow.AddHours(1).AddSeconds(1);
        await recorder.RecordAsync(mismatch);

        repository.Entries.Should().HaveCount(2);

        var next = repository.Entries[1];
        next.Action.Should().Be(AuditAction.MfaSessionDeviceMismatch);
        JsonNode.Parse(next.After!)!["suppressedSincePrevious"]!.GetValue<int>()
            .Should().Be(3, "the refusals the cap held back must stay visible as a number, not vanish");
    }

    [Fact]
    public async Task ShouldWriteToleratedIpChanges_OnTheirOwnBudget()
    {
        // BLUM-7848. A user behind a proxy pool roams on most requests; if those rows shared the refusal
        // budget, the one refusal that matters could be the one the cap holds back.
        var repository = new CapturingAuditLogRepository();
        var recorder = RecorderFor(repository, perUserCap: 1, out _);
        var mismatch = Mismatch();

        await recorder.RecordToleratedIpChangeAsync(mismatch);
        await recorder.RecordToleratedIpChangeAsync(mismatch);
        await recorder.RecordAsync(mismatch);

        repository.Entries.Select(x => x.Action).Should().Equal(AuditAction.MfaSessionIpChanged, AuditAction.MfaSessionDeviceMismatch);
    }

    [Fact]
    public async Task ShouldNotThrow_WhenTheAuditWriteFails()
    {
        // It runs while a 403 MFA_REQUIRED is already decided. A failure to record that refusal escaping here
        // would turn it into a 500, which is strictly worse than losing the row.
        var recorder = RecorderFor(new FailingAuditLogRepository(), perUserCap: 0, out _);

        var act = () => recorder.RecordAsync(Mismatch());

        await act.Should().NotThrowAsync();
    }

    private static MfaDeviceMismatch Mismatch() =>
        new(
            MfaSessionId.New(),
            UserId.New(),
            RecordedIpAddress: "203.0.113.10",
            CurrentIpAddress: "198.51.100.20",
            RecordedDeviceLabel: "Chrome on Windows",
            CurrentDeviceLabel: "Chrome on Windows",
            IpAddressMatches: false,
            UserAgentMatches: true,
            SessionAgeSeconds: 600,
            IdleSeconds: 240);

    private static MfaDeviceMismatchRecorder RecorderFor(IAuditLogRepository repository, int perUserCap, out TestClock clock)
    {
        clock = new TestClock();

        var settings = new ApplicationSettings
        {
            Mfa = new MfaSettings
            {
                MaxDeviceMismatchAuditRowsPerUserPerHour = perUserCap,
                MaxDeviceMismatchAuditRowsPerHour = 0,
            },
        };

        // No connection string, so TrackEventIfEnabled sends nothing — the telemetry path runs, it just has
        // nowhere to go, exactly as in dev and test.
        var telemetryClient = new TelemetryClient(new TelemetryConfiguration());
        var throttle = new AuditFloodThrottle(new MemoryCache(new MemoryCacheOptions()), new FixedWindowCounter(clock));

        return new MfaDeviceMismatchRecorder(repository, telemetryClient, throttle, settings, NullLogger<MfaDeviceMismatchRecorder>.Instance);
    }

    private sealed class CapturingAuditLogRepository : IAuditLogRepository
    {
        public List<AuditLog> Entries { get; } = [];

        public Task<AuditLog> AppendAsync(AuditLog entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.FromResult(entry);
        }

        public Task<bool> TryAppendAsync(AuditLog entry)
        {
            Entries.Add(entry);
            return Task.FromResult(true);
        }

        public Task<AuditLogDeletionResult> DeleteOccurredBeforeAsync(DateTime cutoff, int batchSize, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FailingAuditLogRepository : IAuditLogRepository
    {
        public Task<AuditLog> AppendAsync(AuditLog entry, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("audit_logs is unavailable");

        public Task<bool> TryAppendAsync(AuditLog entry) => Task.FromResult(false);

        public Task<AuditLogDeletionResult> DeleteOccurredBeforeAsync(DateTime cutoff, int batchSize, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}