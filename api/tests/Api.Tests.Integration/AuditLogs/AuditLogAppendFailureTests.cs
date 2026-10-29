using Application.Common.Interfaces.Repositories;
using Domain.AuditLogs;
using Domain.Users;
using FluentAssertions;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tests.Common;
using Xunit;

namespace Api.Tests.Integration.AuditLogs;

/// <summary>
/// BLUM-7848 review — a failed audit append must not stay tracked in the request-scoped context.
///
/// Several callers swallow an audit failure on purpose and let the request carry on. Since the IP binding
/// was relaxed, that includes requests which then reach a handler, and the handler's own SaveChangesAsync
/// would retry the stuck insert — failing, or rolling back the user's actual change with it.
/// </summary>
public class AuditLogAppendFailureTests(IntegrationTestWebFactory factory)
    : BaseIntegrationTest(factory), IAsyncLifetime
{
    [Fact]
    public async Task ShouldNotLeaveAFailedAppendTracked_SoTheNextSaveSucceeds()
    {
        // Repository and context from one scope, as a request would have them.
        await using var scope = factory.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var entry = AuditLog.Create(UserId.New(), AuditTargetType.MfaSession, Guid.NewGuid(), AuditAction.MfaSessionIpChanged, before: null, after: null);
        await repository.AppendAsync(entry);

        // Appending the same row again is a primary-key violation: a dependable way to make the insert fail.
        var duplicate = () => repository.AppendAsync(entry);
        await duplicate.Should().ThrowAsync<Exception>();

        context.ChangeTracker.Entries<AuditLog>().Should().NotContain(x => x.State == EntityState.Added);

        var nextSave = () => context.SaveChangesAsync();
        await nextSave.Should().NotThrowAsync();
    }

    // BLUM-8747: handlers call TryAppendAsync after their change is committed, so a failed append must come back
    // as false — an exception there would answer a 500 for a change that went through.
    [Fact]
    public async Task TryAppend_ShouldReturnFalseRatherThanThrow_AndLeaveNothingTracked()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var entry = AuditLog.Create(UserId.New(), AuditTargetType.Submission, Guid.NewGuid(), AuditAction.SubmissionSubmitted, before: null, after: null);
        (await repository.TryAppendAsync(entry)).Should().BeTrue();

        (await repository.TryAppendAsync(entry)).Should().BeFalse();

        context.ChangeTracker.Entries<AuditLog>().Should().NotContain(x => x.State == EntityState.Added);
        var nextSave = () => context.SaveChangesAsync();
        await nextSave.Should().NotThrowAsync();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        Context.AuditLogs.RemoveRange(Context.AuditLogs);
        await SaveChangesAsync();
    }
}