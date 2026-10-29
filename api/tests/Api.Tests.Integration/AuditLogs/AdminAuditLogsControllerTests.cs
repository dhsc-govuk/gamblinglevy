using System.Net;
using System.Text.Json;
using Application.AuditLogs;
using Application.AuditLogs.Dtos;
using Domain.AuditLogs;
using Domain.Organisations;
using Domain.Users;
using Domain.ValueObjects;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Tests.Common;
using Xunit;

namespace Api.Tests.Integration.AuditLogs;

/// <summary>
/// BLUM-7775 — the "Sort by" control the redesigned Audit log page adds.
///
/// The ordering has to be decided in SQL, before Skip/Take. Reversing the page in the browser would leave
/// "Oldest" showing the fifty newest rows in reverse, which looks right on the only page most environments
/// have and is wrong everywhere else — ShouldApplyTheSortBeforePaging is the case that tells the two apart.
/// </summary>
public class AdminAuditLogsControllerTests(IntegrationTestWebFactory factory)
    : BaseIntegrationTest(factory), IAsyncLifetime
{
    private const string Route = "admin/audit-logs";

    private static readonly UserId Actor = UserId.New();

    // Fixed instants, deliberately out of insertion order, so a test that merely echoed the seed order
    // would not pass. Four of them rather than three: with an odd number the middle row is second from
    // both ends, and ShouldApplyTheSortBeforePaging passes against an unsorted query by coincidence.
    private static readonly DateTime Oldest = new(2026, 8, 21, 15, 8, 0, DateTimeKind.Utc);
    private static readonly DateTime SecondOldest = new(2026, 8, 21, 15, 22, 0, DateTimeKind.Utc);
    private static readonly DateTime SecondNewest = new(2026, 8, 26, 8, 8, 43, DateTimeKind.Utc);
    private static readonly DateTime Newest = new(2026, 8, 27, 16, 23, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("")]
    [InlineData("?sort=newest")]
    public async Task ShouldReturnNewestFirst_WhenSortIsAbsentOrNewest(string query)
    {
        // Act
        var response = await Client.GetAsync(Route + query);

        // Assert
        response.IsSuccessStatusCode.Should().BeTrue();
        var page = await response.ToResponseModel<PagedAuditLogs<AuditLogListItemDto>>();

        page.TotalCount.Should().Be(4);
        page.Items.Select(x => x.OccurredAt).Should().ContainInOrder(Newest, SecondNewest, SecondOldest, Oldest);
    }

    [Fact]
    public async Task ShouldReturnOldestFirst_WhenSortIsOldest()
    {
        // Act
        var response = await Client.GetAsync($"{Route}?sort={AuditLogSort.Oldest}");

        // Assert
        response.IsSuccessStatusCode.Should().BeTrue();
        var page = await response.ToResponseModel<PagedAuditLogs<AuditLogListItemDto>>();

        page.Items.Select(x => x.OccurredAt).Should().ContainInOrder(Oldest, SecondOldest, SecondNewest, Newest);
    }

    [Fact]
    public async Task ShouldApplyTheSortBeforePaging()
    {
        // Act — the second page of one row, oldest first.
        var response = await Client.GetAsync($"{Route}?sort={AuditLogSort.Oldest}&page=2&pageSize=1");

        // Assert — the second-oldest row. A sort applied to an already-taken page, or no sort at all,
        // would answer with the second-newest instead.
        response.IsSuccessStatusCode.Should().BeTrue();
        var page = await response.ToResponseModel<PagedAuditLogs<AuditLogListItemDto>>();

        page.Items.Should().ContainSingle();
        page.Items[0].OccurredAt.Should().Be(SecondOldest);
    }

    [Fact]
    public async Task ShouldRejectAnUnrecognisedSort_WithTheValidationFailedEnvelope()
    {
        // Act
        var response = await Client.GetAsync($"{Route}?sort=sideways");

        // Assert — a bookmarked ?sort=bogus must not quietly answer in the opposite order.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        root.GetProperty("error").GetString().Should().Be("validation_failed");
        root.GetProperty("errors")
            .EnumerateArray()
            .Should()
            .Contain(item => item.GetProperty("property").GetString() == "sort");
    }

    [Fact]
    public async Task ShouldReturnAnAuditLogById_WithItsSnapshots()
    {
        // Arrange — a row whose actor and organisation have since been deleted. audit_logs holds no foreign
        // key to either, which is the point of it, so the row must still be readable.
        var occurredAt = new DateTime(2026, 8, 28, 10, 30, 0, DateTimeKind.Utc);
        var organisationId = OrganisationId.New();
        var targetId = Guid.NewGuid();
        var log = AuditLog.Create(
            Actor,
            AuditTargetType.Organisation,
            targetId,
            AuditAction.OrganisationUpdated,
            before: """{"name":"Old Council"}""",
            after: """{"name":"New Council"}""",
            organisationId);

        var entry = await Context.AuditLogs.AddAsync(log);
        entry.Property(x => x.OccurredAt).CurrentValue = occurredAt;
        await SaveChangesAsync();

        // Act
        var response = await Client.GetAsync($"{Route}/{log.Id.Value}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = await response.ToResponseModel<AuditLogDetailDto>();

        detail.Id.Should().Be(log.Id.Value);
        detail.OccurredAt.Should().Be(occurredAt);
        detail.Action.Should().Be(AuditAction.OrganisationUpdated);
        detail.ActorUserId.Should().Be(Actor.Value);
        detail.TargetType.Should().Be(AuditTargetType.Organisation);
        detail.TargetId.Should().Be(targetId);
        detail.OrganisationId.Should().Be(organisationId.Value);

        detail.ActorDisplayName.Should().BeNull();
        detail.TargetDisplayName.Should().BeNull();
        detail.OrganisationName.Should().BeNull();

        // jsonb re-spaces what was stored, so the snapshots are compared as JSON.
        using var before = JsonDocument.Parse(detail.Before!);
        using var after = JsonDocument.Parse(detail.After!);
        before.RootElement.GetProperty("name").GetString().Should().Be("Old Council");
        after.RootElement.GetProperty("name").GetString().Should().Be("New Council");
    }

    [Fact]
    public async Task ShouldReturn404_ForAnUnknownAuditLogId()
    {
        // Act
        var response = await Client.GetAsync($"{Route}/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(Role.OrganisationAdmin)]
    [InlineData(Role.Lead)]
    public async Task ShouldForbidOrganisationRoles_FromTheDhscAuditLog(string role)
    {
        // Arrange — this surface is unscoped: it answers with every organisation's rows. An LA reads its own
        // on organisation-admin/audit-logs.
        var existing = await Context.AuditLogs.AsNoTracking().Select(x => x.Id).FirstAsync();
        AuthenticateAs(role, Guid.NewGuid(), Guid.NewGuid());

        // Act
        var list = await Client.GetAsync(Route);
        var detail = await Client.GetAsync($"{Route}/{existing.Value}");

        // Assert
        list.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        detail.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    public async Task InitializeAsync()
    {
        AuthenticateAsAdmin();

        Context.AuditLogs.RemoveRange(Context.AuditLogs);
        await SaveChangesAsync();

        await SeedAsync(AuditAction.ActivityUpdated, SecondOldest);
        await SeedAsync(AuditAction.ActivityCreated, Newest);
        await SeedAsync(AuditAction.OrganisationBulkImported, Oldest);
        await SeedAsync(AuditAction.CollectionPublished, SecondNewest);
    }

    public async Task DisposeAsync()
    {
        Context.AuditLogs.RemoveRange(Context.AuditLogs);
        await SaveChangesAsync();
        AuthenticateAsAdmin();
    }

    // AuditLog.Create takes no time: the repository stamps it on append and the entity exposes no way to set
    // it — correct for production, useless for an ordering test — so the instant is written through the
    // change tracker, the same route the repository uses.
    private async Task SeedAsync(string action, DateTime occurredAt)
    {
        var entry = await Context.AuditLogs.AddAsync(
            AuditLog.Create(Actor, AuditTargetType.Activity, Guid.NewGuid(), action, before: null, after: null));

        entry.Property(x => x.OccurredAt).CurrentValue = occurredAt;
        await SaveChangesAsync();
    }
}