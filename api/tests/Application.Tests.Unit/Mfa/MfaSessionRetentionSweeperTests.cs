using Application.Common.Interfaces.Repositories;
using Application.Common.Settings;
using Application.Tests.Unit.Security;
using Domain.Users;
using FluentAssertions;
using Infrastructure.Mfa;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Application.Tests.Unit.Mfa;

/// <summary>
/// BLUM-7241 — the sweep's batch loop. Driven against a stub repository rather than a database because the
/// behaviour that matters is which of the two returned counts the loop believes, and the case that broke it
/// (a second instance deleting some of the same rows between the select and the delete) cannot be staged
/// from outside the repository call.
/// </summary>
public class MfaSessionRetentionSweeperTests
{
    [Fact]
    public async Task ShouldKeepGoing_WhenAConcurrentSweepDeletedSomeOfTheBatch()
    {
        // The regression this exists for. The loop used to exit on the DELETE count, so a rival instance that
        // removed part of a full batch first made this sweep conclude there was nothing left — abandoning
        // exactly the large first-run backlog the loop exists to clear.
        var repository = new StubRepository(
            new SessionDeletionResult(Found: 10, Deleted: 3),
            new SessionDeletionResult(Found: 10, Deleted: 0),
            new SessionDeletionResult(Found: 4, Deleted: 4));

        var deleted = await SweeperFor(repository, batchSize: 10).SweepAsync();

        repository.Calls.Should().Be(3, "a full batch found means there is more to do, whatever this call managed to delete");
        deleted.Should().Be(7);
    }

    [Fact]
    public async Task ShouldStop_WhenTheCutoffMatchesFewerThanTheBatchSize()
    {
        var repository = new StubRepository(new SessionDeletionResult(Found: 2, Deleted: 2));

        var deleted = await SweeperFor(repository, batchSize: 10).SweepAsync();

        repository.Calls.Should().Be(1);
        deleted.Should().Be(2);
    }

    [Fact]
    public async Task ShouldStopAfterTheBatchCeiling_RatherThanLoopForever()
    {
        // A clock jumping backwards or a cutoff misconfigured into the future could hand back full batches
        // indefinitely. Whatever is left waits for the next run rather than this one deleting the table.
        var repository = new StubRepository(alwaysFullBatch: true);

        await SweeperFor(repository, batchSize: 10).SweepAsync();

        repository.Calls.Should().BeLessThan(50);
    }

    [Fact]
    public async Task ShouldDoNothing_WhenRetentionIsDisabled()
    {
        var repository = new StubRepository(alwaysFullBatch: true);

        var deleted = await SweeperFor(repository, batchSize: 10, retentionDays: 0).SweepAsync();

        repository.Calls.Should().Be(0);
        deleted.Should().Be(0);
    }

    private static MfaSessionRetentionSweeper SweeperFor(StubRepository repository, int batchSize, int retentionDays = 30)
    {
        var settings = new ApplicationSettings
        {
            Mfa = new MfaSettings
            {
                SessionRetentionDays = retentionDays,
                SessionRetentionSweepBatchSize = batchSize,
            },
        };

        return new MfaSessionRetentionSweeper(repository, new TestClock(), settings, NullLogger<MfaSessionRetentionSweeper>.Instance);
    }

    private sealed class StubRepository : IMfaSessionRepository
    {
        private readonly Queue<SessionDeletionResult> _results;
        private readonly bool _alwaysFullBatch;

        public StubRepository(params SessionDeletionResult[] results)
        {
            _results = new Queue<SessionDeletionResult>(results);
        }

        public StubRepository(bool alwaysFullBatch)
        {
            _results = new Queue<SessionDeletionResult>();
            _alwaysFullBatch = alwaysFullBatch;
        }

        public int Calls { get; private set; }

        public Task<SessionDeletionResult> DeleteEndedSessionsAsync(DateTime endedBefore, int batchSize, CancellationToken cancellationToken = default)
        {
            Calls++;

            if (_alwaysFullBatch)
            {
                return Task.FromResult(new SessionDeletionResult(batchSize, batchSize));
            }

            return Task.FromResult(_results.Count > 0 ? _results.Dequeue() : new SessionDeletionResult(0, 0));
        }

        public Task<MfaSession?> GetByIdAsync(MfaSessionId sessionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<List<MfaSession>> GetByUserIdAsync(UserId userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<List<MfaSession>> GetActiveByUserIdAsync(UserId userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<MfaSession> AddAsync(MfaSession session, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<MfaSession> UpdateAsync(MfaSession session, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<int> TryRevokeTimedOutSessionAsync(MfaSessionId sessionId, MfaSessionEndReason reason, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<int> TryRevokeSessionAsync(MfaSessionId sessionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<int> TryUpdateLastActivityAsync(MfaSessionId sessionId, DateTime lastActivityAt, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<int> TryUpdateLastSeenIpAsync(MfaSessionId sessionId, string? expectedHash, string newHash, string newIpAddress, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}