using System.Net;
using Application.AuditLogs.Dtos;
using Application.Common.Interfaces.Repositories;
using Domain.AuditLogs;
using Domain.Users;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tests.Common;
using Xunit;

namespace Api.Tests.Integration.AuditLogs;

/// <summary>
/// BLUM-8750 — every audit row says which version of its action's Before/After shape it holds. Rows written
/// before the column existed have to read as the initial version too, which is the column default's job.
/// </summary>
public class AuditLogPayloadVersionTests(IntegrationTestWebFactory factory)
    : BaseIntegrationTest(factory), IAsyncLifetime
{
    private const string Route = "admin/audit-logs";

    private static readonly UserId Actor = new(Guid.Parse("b8750000-0000-0000-0000-000000000002"));

    [Fact]
    public async Task ShouldWriteTheInitialVersion_WhenAppendingThroughTheRepository()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();
        var entry = NewEntry();

        await repository.AppendAsync(entry);

        var saved = await Context.AuditLogs.AsNoTracking().SingleAsync(x => x.Id == entry.Id);
        saved.PayloadVersion.Should().Be(AuditLog.InitialPayloadVersion);
    }

    [Fact]
    public async Task ShouldKeepAnExplicitVersion()
    {
        var entry = NewEntry(payloadVersion: 2);

        await Context.AuditLogs.AddAsync(entry);
        await SaveChangesAsync();

        var saved = await Context.AuditLogs.AsNoTracking().SingleAsync(x => x.Id == entry.Id);
        saved.PayloadVersion.Should().Be(2);
    }

    [Fact]
    public async Task ShouldReadExistingRowsAsTheInitialVersion()
    {
        // A row inserted without the column, as every row written before the migration was.
        var id = Guid.NewGuid();
        await Context.Database.ExecuteSqlAsync(
            $"INSERT INTO audit_logs (id, actor_user_id, target_type, target_id, action) VALUES ({id}, {Actor.Value}, {AuditTargetType.User}, {Guid.NewGuid()}, {AuditAction.UserUpdated})");

        var saved = await Context.AuditLogs.AsNoTracking().SingleAsync(x => x.Id == new AuditLogId(id));
        saved.PayloadVersion.Should().Be(AuditLog.InitialPayloadVersion);
    }

    [Fact]
    public async Task ShouldReturnThePayloadVersion_OnTheDetailEndpoint()
    {
        var entry = NewEntry(payloadVersion: 3);
        await Context.AuditLogs.AddAsync(entry);
        await SaveChangesAsync();

        var response = await Client.GetAsync($"{Route}/{entry.Id.Value}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = await response.ToResponseModel<AuditLogDetailDto>();
        detail.PayloadVersion.Should().Be(3);
    }

    private static AuditLog NewEntry(int payloadVersion = AuditLog.InitialPayloadVersion) =>
        AuditLog.Create(
            Actor,
            AuditTargetType.User,
            Guid.NewGuid(),
            AuditAction.UserUpdated,
            before: null,
            after: """{"firstName":"Ada"}""",
            payloadVersion: payloadVersion);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        Context.AuditLogs.RemoveRange(Context.AuditLogs);
        await SaveChangesAsync();
        AuthenticateAsAdmin();
    }
}