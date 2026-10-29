using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.Tests.Unit.Common;
using Application.Tests.Unit.Security;
using Application.Users;
using Domain.AuditLogs;
using Domain.Users;
using Domain.ValueObjects;
using FluentAssertions;
using LanguageExt;
using Xunit;

namespace Application.Tests.Unit.Users;

/// <summary>
/// BLUM-7667 — the order of the deactivation sequence. Driven against recording fakes rather than a database,
/// because the behaviour that matters is what has and has not happened when one step fails, and a failing
/// session revocation cannot be staged through the API.
/// </summary>
public class UserDeactivatorTests
{
    private readonly List<string> _calls = [];

    [Fact]
    public async Task ShouldLeaveTheUserActive_WhenRevokingTheirSessionsFails()
    {
        // The regression this exists for. Sessions used to be revoked after the status commit, so a failure
        // there left a deactivated user with live sessions. An organisation-wide retry only picks up users who
        // are still active, so it never revoked them, and reactivating that user later would have skipped MFA
        // re-verification. Failing before anything is committed keeps the user where a retry can see them.
        var user = ActiveUser();
        var deactivator = DeactivatorFor(revocationFails: true);

        var act = () => deactivator.DeactivateAsync(user, UserId.New());

        await act.Should().ThrowAsync<InvalidOperationException>();
        user.Status.Should().Be(UserStatus.Active);
        _calls.Should().Equal("revoke", "rollback");
    }

    [Fact]
    public async Task ShouldRevokeSessions_ThenCommitWithTheAuditRow_ThenInvalidateTheCache()
    {
        var user = ActiveUser();

        await DeactivatorFor().DeactivateAsync(user, UserId.New());

        user.Status.Should().Be(UserStatus.Deactivated);

        // One transaction (BLUM-8748): revocation, the staged status and its audit row commit together, and the
        // cache is only cleared once they have.
        _calls.Should().Equal("revoke", "stage", "append", "commit", "invalidate");
    }

    [Fact]
    public async Task ShouldLeaveTheCacheToTheCaller_WhenItJoinedTheCallersTransaction()
    {
        // The organisation cascade deactivates inside its own transaction. Clearing the cache here would let a
        // request before that commit re-read the user as active and cache it for the full TTL, so the caller
        // invalidates after committing instead.
        var user = ActiveUser();
        var deactivator = DeactivatorFor(inCallersTransaction: true);

        await deactivator.DeactivateAsync(user, UserId.New());
        _calls.Should().Equal("revoke", "stage", "append");

        deactivator.InvalidateClaims([user]);
        _calls.Should().Equal("revoke", "stage", "append", "invalidate");
    }

    private static User ActiveUser() => User.New(
        UserId.New(),
        new Name("Leaving", "User"),
        "leaving.user@council.gov.uk",
        new SubId(Guid.NewGuid()),
        UserStatus.Active,
        UserRole.Lead,
        DateTime.UtcNow);

    private UserDeactivator DeactivatorFor(bool revocationFails = false, bool inCallersTransaction = false) => new(
        new RecordingUserRepository(_calls),
        new RecordingAuditLogRepository(_calls),
        new RecordingMfaSessionService(_calls, revocationFails),
        new RecordingClaimsCache(_calls),
        new TestClock(),
        new InlineTransactionRunner(_calls) { InTransaction = inCallersTransaction });

    private sealed class RecordingMfaSessionService(List<string> calls, bool fails) : IMfaSessionService
    {
        public Task RevokeAllUserSessionsAsync(UserId userId)
        {
            calls.Add("revoke");
            return fails ? Task.FromException(new InvalidOperationException("revocation failed")) : Task.CompletedTask;
        }

        public string HashIpAddress(string ipAddress) => throw new NotSupportedException();

        public string HashUserAgent(string userAgent) => throw new NotSupportedException();

        public Task<MfaSession> CreateSessionAsync(UserId userId, string ipAddress, string userAgent) => throw new NotSupportedException();

        public Task<MfaSessionStatus> ValidateSessionAsync(MfaSessionId sessionId, UserId userId, string ipAddress, string userAgent) =>
            throw new NotSupportedException();

        public Task<MfaSessionRevocationResult> RevokeSessionAsync(MfaSessionId sessionId, UserId userId) => throw new NotSupportedException();
    }

    private sealed class RecordingUserRepository(List<string> calls) : IUserRepository
    {
        public Task<int> UpdateRangeAsync(IReadOnlyList<User> entities, CancellationToken cancellationToken = default, bool saveChanges = true)
        {
            // The sequence depends on this staying unsaved until the audit append commits it.
            saveChanges.Should().BeFalse();
            calls.Add("stage");
            return Task.FromResult(entities.Count);
        }

        public Task<Option<User>> FindById(UserId id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Option<User>> FindInScope(UserId id, Permission permission, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Option<User>> FindByEmailAsync(string email, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Option<User>> FindDuplicateAsync(string targetEmail, UserId sourceId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<User>> GetActiveAdminsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<User>> GetAllAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<User> AddAsync(User entity, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<User> UpdateAsync(User entity, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<User> RemoveAsync(User entity, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class RecordingAuditLogRepository(List<string> calls) : IAuditLogRepository
    {
        public Task<AuditLog> AppendAsync(AuditLog entry, CancellationToken cancellationToken = default)
        {
            calls.Add("append");
            return Task.FromResult(entry);
        }

        public Task<bool> TryAppendAsync(AuditLog entry)
        {
            calls.Add("append");
            return Task.FromResult(true);
        }

        public Task<AuditLogDeletionResult> DeleteOccurredBeforeAsync(DateTime cutoff, int batchSize, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingClaimsCache(List<string> calls) : IUserClaimsCache
    {
        public void Invalidate(SubId subId) => calls.Add("invalidate");
    }
}