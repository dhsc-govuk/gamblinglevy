using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Domain.AuditLogs;
using Domain.Users;
using Domain.ValueObjects;
using FluentAssertions;
using Infrastructure.Persistence;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tests.Common;
using Xunit;

namespace Api.Tests.Integration.AuditLogs;

/// <summary>
/// BLUM-8748 — an audited change and its audit row commit in one transaction, or neither does.
///
/// Driven through the runner and the real repositories in one request-shaped scope rather than over HTTP: what
/// matters is what is left in the database when one half fails, and an audit insert cannot be made to fail through
/// the API. A duplicate primary key is the dependable way to fail it, as in AuditLogAppendFailureTests.
/// </summary>
public class AuditTransactionTests(IntegrationTestWebFactory factory)
    : BaseIntegrationTest(factory), IAsyncLifetime
{
    private readonly List<UserId> _users = [];

    [Fact]
    public async Task ShouldRollBackTheChange_WhenItsAuditRowCannotBeWritten()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var (runner, users, audit) = Resolve(scope);
        var existing = await AppendCommittedRowAsync(audit);
        var user = NewUser();

        var act = () => runner.RunAsync<Exception, User>(
            async token =>
            {
                await users.AddAsync(user, token);
                await audit.AppendAsync(existing, token);
                return user;
            },
            CancellationToken.None);

        await act.Should().ThrowAsync<Exception>();
        (await UserExistsAsync(user.Id)).Should().BeFalse("the change must not outlive the audit row that records it");
    }

    [Fact]
    public async Task ShouldKeepNeitherTheChangeNorItsAuditRow_WhenTheWorkReturnsLeft()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var (runner, users, audit) = Resolve(scope);
        var user = NewUser();

        var result = await runner.RunAsync<string, User>(
            async token =>
            {
                await users.AddAsync(user, token);
                await audit.AppendAsync(UserCreatedRow(user), token);
                return "refused after both writes";
            },
            CancellationToken.None);

        result.IsLeft.Should().BeTrue();
        (await UserExistsAsync(user.Id)).Should().BeFalse();
        (await AuditRowsForAsync(user.Id)).Should().BeEmpty("a row for a change that never happened is the other half of the defect");
    }

    [Fact]
    public async Task ShouldCommitTheChangeAndItsAuditRowTogether()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var (runner, users, audit) = Resolve(scope);
        var user = NewUser();

        var result = await runner.RunAsync<string, User>(
            async token =>
            {
                await users.AddAsync(user, token);
                await audit.AppendAsync(UserCreatedRow(user), token);
                return user;
            },
            CancellationToken.None);

        result.IsRight.Should().BeTrue();
        (await UserExistsAsync(user.Id)).Should().BeTrue();
        (await AuditRowsForAsync(user.Id)).Should().ContainSingle(x => x.Action == AuditAction.UserCreated);
    }

    [Fact]
    public async Task ShouldJoinAnOpenTransaction_SoTheOuterLeftUndoesTheInnerWork()
    {
        // A shared collaborator (UserDeactivator, IMfaSessionService) runs its own RunAsync; inside a handler's it
        // must join rather than commit on its own, or the handler's rollback would leave its part behind.
        await using var scope = factory.Services.CreateAsyncScope();
        var (runner, users, audit) = Resolve(scope);
        var user = NewUser();

        var result = await runner.RunAsync<string, Unit>(
            async token =>
            {
                var inner = await runner.RunAsync<string, User>(
                    async innerToken =>
                    {
                        await users.AddAsync(user, innerToken);
                        await audit.AppendAsync(UserCreatedRow(user), innerToken);
                        return user;
                    },
                    token);

                inner.IsRight.Should().BeTrue();
                runner.InTransaction.Should().BeTrue("the inner run joined and must not have committed");
                return "the outer change failed after the inner one";
            },
            CancellationToken.None);

        result.IsLeft.Should().BeTrue();
        (await UserExistsAsync(user.Id)).Should().BeFalse();
        (await AuditRowsForAsync(user.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task TryAppend_ShouldRefuseToRunInsideATransaction()
    {
        // A swallowed failure aborts a PostgreSQL transaction (25P02), sinking the change it was meant to record.
        await using var scope = factory.Services.CreateAsyncScope();
        var (runner, users, audit) = Resolve(scope);
        var user = NewUser();

        var act = () => runner.RunAsync<string, User>(
            async token =>
            {
                await users.AddAsync(user, token);
                await audit.TryAppendAsync(UserCreatedRow(user));
                return user;
            },
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await UserExistsAsync(user.Id)).Should().BeFalse();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        Context.AuditLogs.RemoveRange(Context.AuditLogs);
        Context.Users.RemoveRange(Context.Users.Where(x => _users.Contains(x.Id)));
        await SaveChangesAsync();
    }

    private static (ITransactionRunner Runner, IUserRepository Users, IAuditLogRepository Audit) Resolve(AsyncServiceScope scope)
    {
        var services = scope.ServiceProvider;
        return (services.GetRequiredService<ITransactionRunner>(), services.GetRequiredService<IUserRepository>(), services.GetRequiredService<IAuditLogRepository>());
    }

    private static AuditLog UserCreatedRow(User user) =>
        AuditLog.Create(UserId.New(), AuditTargetType.User, user.Id.Value, AuditAction.UserCreated, before: null, after: null);

    private static async Task<AuditLog> AppendCommittedRowAsync(IAuditLogRepository audit)
    {
        var row = AuditLog.Create(UserId.New(), AuditTargetType.User, Guid.NewGuid(), AuditAction.UserCreated, before: null, after: null);
        await audit.AppendAsync(row);
        return row;
    }

    private User NewUser()
    {
        var id = UserId.New();
        _users.Add(id);
        return User.New(
            id,
            new Name("Audited", "Change"),
            $"audited.{id.Value:N}@council.gov.uk",
            new SubId(Guid.NewGuid()),
            UserStatus.Active,
            UserRole.Lead,
            DateTime.UtcNow);
    }

    private async Task<bool> UserExistsAsync(UserId id)
    {
        return await Context.Users.AsNoTracking().AnyAsync(x => x.Id == id);
    }

    private async Task<List<AuditLog>> AuditRowsForAsync(UserId id)
    {
        return await Context.AuditLogs.AsNoTracking().Where(x => x.TargetId == id.Value).ToListAsync();
    }
}