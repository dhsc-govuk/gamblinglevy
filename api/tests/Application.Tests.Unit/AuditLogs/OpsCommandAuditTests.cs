using System.Reflection;
using System.Text.Json;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.DataReset.Commands.ResetTestData;
using Application.Tests.Unit.Security;
using Application.UserEmailNormalisation.Commands.NormaliseUserEmails;
using Application.Users.Interfaces;
using Domain.AuditLogs;
using Domain.Users;
using Domain.ValueObjects;
using FluentAssertions;
using LanguageExt;
using Xunit;

namespace Application.Tests.Unit.AuditLogs;

// BLUM-8747 review: the ops endpoints commit as they go, so a run that fails part-way has already changed something
// and must still leave an audit row saying so.
public class OpsCommandAuditTests
{
    [Fact]
    public async Task NormaliseUserEmails_ThatFailsPartWay_StillAuditsTheUsersAlreadyCorrected()
    {
        var first = LegacyUser("First.User@Example.gov.uk");
        var second = LegacyUser("Second.User@Example.gov.uk");
        var audit = new CapturingAuditLogRepository();
        var handler = new NormaliseUserEmailsCommandHandler(
            new FailingOnSecondUpdateUserRepository([first, second], second.Id),
            new SucceedingUsersService(),
            audit,
            new TestClock());

        var result = await handler.Handle(new NormaliseUserEmailsCommand(DryRun: false), CancellationToken.None);

        result.IsLeft.Should().BeTrue("the second user's local update threw");
        var row = audit.Entries.Should().ContainSingle().Subject;
        row.Action.Should().Be(AuditAction.SystemUserEmailsNormalised);

        using var after = JsonDocument.Parse(row.After!);
        after.RootElement.GetProperty("completed").GetBoolean().Should().BeFalse();
        after.RootElement.GetProperty("updatedCount").GetInt32().Should().Be(1);
        after.RootElement.GetProperty("updatedUserIds")[0].GetGuid().Should().Be(first.Id.Value);
        after.RootElement.GetProperty("interruptedAtUserId").GetGuid().Should().Be(
            second.Id.Value,
            "Entra was corrected for this user before its local row failed to follow");
        row.After.Should().NotContain("@");
    }

    [Fact]
    public async Task NormaliseUserEmails_DryRun_WritesNoRowEvenWhenItFails()
    {
        var audit = new CapturingAuditLogRepository();
        var handler = new NormaliseUserEmailsCommandHandler(
            new FailingOnSecondUpdateUserRepository([LegacyUser("Only.User@Example.gov.uk")], UserId.New()),
            new SucceedingUsersService(),
            audit,
            new TestClock());

        await handler.Handle(new NormaliseUserEmailsCommand(DryRun: true), CancellationToken.None);

        audit.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task ResetTestData_ThatFails_StillAuditsTheAttempt_WithTheExceptionTypeOnly()
    {
        var audit = new CapturingAuditLogRepository();
        var handler = new ResetTestDataCommandHandler(new ThrowingDataResetService(), audit);

        var result = await handler.Handle(new ResetTestDataCommand(), CancellationToken.None);

        result.IsLeft.Should().BeTrue();
        var row = audit.Entries.Should().ContainSingle().Subject;
        row.Action.Should().Be(AuditAction.SystemTestDataReset);

        using var after = JsonDocument.Parse(row.After!);
        after.RootElement.GetProperty("succeeded").GetBoolean().Should().BeFalse();
        after.RootElement.GetProperty("error").GetString().Should().Be(nameof(InvalidOperationException));
        row.After.Should().NotContain(ThrowingDataResetService.Message, "the message can carry SQL and row values");
    }

    // User.New lower-cases the address, as every write path now does; a pre-BLUM-7869 row did not, so the mixed-case
    // value is put back the way the integration test's raw SQL does it.
    private static User LegacyUser(string mixedCaseEmail)
    {
        var user = User.New(
            UserId.New(), new Name("Legacy", "User"), mixedCaseEmail, new SubId(Guid.NewGuid()), UserStatus.Active, UserRole.Lead, DateTime.UtcNow);
        typeof(User).GetProperty(nameof(User.Email), BindingFlags.Instance | BindingFlags.Public)!.SetValue(user, mixedCaseEmail);
        return user;
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

    private sealed class FailingOnSecondUpdateUserRepository(IReadOnlyList<User> users, UserId failFor) : IUserRepository
    {
        public Task<IReadOnlyList<User>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult(users);

        public Task<User> UpdateAsync(User entity, CancellationToken cancellationToken = default) =>
            entity.Id == failFor ? throw new InvalidOperationException("users is unavailable") : Task.FromResult(entity);

        public Task<Option<User>> FindById(UserId id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Option<User>> FindInScope(UserId id, Permission permission, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Option<User>> FindByEmailAsync(string email, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Option<User>> FindDuplicateAsync(string targetEmail, UserId sourceId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<User>> GetActiveAdminsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<User> AddAsync(User entity, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<int> UpdateRangeAsync(IReadOnlyList<User> entities, CancellationToken cancellationToken = default, bool saveChanges = true) =>
            throw new NotSupportedException();

        public Task<User> RemoveAsync(User entity, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class SucceedingUsersService : IUsersService
    {
        public Task<Either<Exception, LanguageExt.Unit>> Update(string id, Name userName, string email, bool passwordLoginEnabled, CancellationToken cancellationToken) =>
            Task.FromResult<Either<Exception, LanguageExt.Unit>>(LanguageExt.Unit.Default);

        public Task<Option<string>> FindById(string userId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Either<Exception, Option<string>>> FindByEmail(string email, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Either<Exception, string>> Add(Name userName, string email, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Either<Exception, Option<string>>> EnablePasswordLogin(
            string id, Name userName, string email, string temporaryPassword, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Either<Exception, LanguageExt.Unit>> ResetPassword(string id, string email, string temporaryPassword, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Either<Exception, LanguageExt.Unit>> Disable(string id, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Either<Exception, LanguageExt.Unit>> Enable(string id, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Either<Exception, LanguageExt.Unit>> HardDelete(string id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class ThrowingDataResetService : IDataResetService
    {
        public const string Message = "insert into organisations values ('secret-row')";

        public Task ResetToSeedDataAsync(CancellationToken cancellationToken) => throw new InvalidOperationException(Message);

        public Task<MfaDataCleared> ClearMfaDataAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<UserStatistics> GetUserStatisticsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}