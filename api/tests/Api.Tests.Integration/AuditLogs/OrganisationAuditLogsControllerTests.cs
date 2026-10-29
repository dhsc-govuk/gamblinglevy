using System.Net;
using System.Text.Json;
using Application.AuditLogs;
using Application.AuditLogs.Dtos;
using Domain.AuditLogs;
using Domain.Organisations;
using Domain.OrganisationUsers;
using Domain.Users;
using Domain.ValueObjects;
using FluentAssertions;
using Tests.Common;
using Tests.Data;
using Xunit;

namespace Api.Tests.Integration.AuditLogs;

/// <summary>
/// BLUM-8761 — organisation-admin/audit-logs is the route that decides which local authority sees which
/// audit rows. Nothing in the repository scopes by role: the controller takes the organisation from the
/// caller's token and hands it to the query as a filter, and for a single row compares it after the fetch.
///
/// The seed is built so that losing either of those shows up everywhere. The other LA's row S1 and the
/// organisation-less row N1 carry the same action, actor, target and instant as rows the caller may see, so
/// a query that stopped scoping would return them from every filter test below, not only from the one
/// named for isolation. M4 is an Activity whose id is a user's id, which is what makes the "and the target
/// is a User" half of targetUserId observable.
/// </summary>
public class OrganisationAuditLogsControllerTests(IntegrationTestWebFactory factory)
    : BaseIntegrationTest(factory), IAsyncLifetime
{
    private const string Route = "organisation-admin/audit-logs";

    // Whole seconds, so the round trip through the query string and timestamptz cannot lose anything, and
    // deliberately not the order the rows are inserted in.
    private static readonly DateTime T1 = new(2026, 8, 21, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime T2 = new(2026, 8, 22, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime T3 = new(2026, 8, 23, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime T4 = new(2026, 8, 24, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime T5 = new(2026, 8, 25, 9, 0, 0, DateTimeKind.Utc);

    private static readonly string TooLongAction = new('a', 65);

    private readonly Organisation _mainOrg = OrganisationsData.MainOrganisation;
    private readonly Organisation _secondOrg = OrganisationsData.SecondOrganisation;

    private readonly OrganisationUser _mainAdmin =
        OrganisationUsersData.MainOrganisationUser(OrganisationsData.MainOrganisation.Id);

    private readonly OrganisationUser _mainLead =
        OrganisationUsersData.Member(OrganisationsData.MainOrganisation.Id, UserRole.Lead, "lead@main-council.gov.uk");

    private readonly OrganisationUser _secondAdmin =
        OrganisationUsersData.SecondOrganisationUser(OrganisationsData.SecondOrganisation.Id);

    private Guid _m1;
    private Guid _m2;
    private Guid _m3;
    private Guid _m4;
    private Guid _m5;
    private Guid _s1;
    private Guid _s2;
    private Guid _n1;

    public static TheoryData<string, string> InvalidQueries => new()
    {
        { "page=0", "page" },
        { "page=100001", "page" },
        { "pageSize=0", "pageSize" },
        { "pageSize=201", "pageSize" },
        { $"action={TooLongAction}", "action" },
        { "sort=sideways", "sort" },
    };

    private Guid MainAdminId => _mainAdmin.UserId.Value;

    private Guid MainLeadId => _mainLead.UserId.Value;

    [Theory]
    [InlineData(Role.OrganisationAdmin)]
    [InlineData(Role.Lead)]
    public async Task ShouldListOnlyTheCallersOrganisationsRows(string role)
    {
        // Act
        AuthenticateAs(role, Guid.NewGuid(), _mainOrg.Id.Value);
        var main = await ListAsync(string.Empty);

        AuthenticateAs(role, Guid.NewGuid(), _secondOrg.Id.Value);
        var second = await ListAsync(string.Empty);

        // Assert — each LA gets its own rows and nothing else. N1 belongs to no organisation, so it belongs to
        // neither list: an LA never sees a row DHSC wrote without one.
        main.TotalCount.Should().Be(5);
        Ids(main).Should().BeEquivalentTo([_m1, _m2, _m3, _m4, _m5]);
        main.Items.Should().OnlyContain(x => x.OrganisationId == _mainOrg.Id.Value);

        second.TotalCount.Should().Be(2);
        Ids(second).Should().BeEquivalentTo([_s1, _s2]);
        second.Items.Should().OnlyContain(x => x.OrganisationId == _secondOrg.Id.Value);
    }

    [Fact]
    public async Task ShouldForbidDhscAdmin_FromTheOrganisationAuditLog()
    {
        // Arrange — DHSC reads the audit log on admin/audit-logs. This surface has no organisation to scope a
        // DHSC caller to, so it must refuse rather than answer with everything.
        AuthenticateAsAdmin();

        // Act
        var list = await Client.GetAsync(Route);
        var detail = await Client.GetAsync($"{Route}/{_m1}");

        // Assert
        list.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        detail.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ShouldFilterByExactAction()
    {
        AuthenticateAsMainAdmin();

        // Act
        var matching = await ListAsync($"?action={AuditAction.UserUpdated}");
        var prefixOnly = await ListAsync("?action=user.update");

        // Assert — an exact match, not a prefix or a contains.
        Ids(matching).Should().BeEquivalentTo([_m1, _m2, _m3]);
        matching.TotalCount.Should().Be(3);
        prefixOnly.Items.Should().BeEmpty();
        prefixOnly.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task ShouldFilterByActor()
    {
        AuthenticateAsMainAdmin();

        // Act
        var page = await ListAsync($"?actorUserId={MainAdminId}");

        // Assert
        Ids(page).Should().BeEquivalentTo([_m1, _m3, _m5]);
        page.TotalCount.Should().Be(3);
    }

    [Fact]
    public async Task ShouldFilterByTargetUser_IgnoringOtherTargetTypesWithTheSameId()
    {
        AuthenticateAsMainAdmin();

        // Act
        var page = await ListAsync($"?targetUserId={MainLeadId}");

        // Assert — M4 has the same TargetId but is an Activity, and M3 is a User row about someone else.
        Ids(page).Should().BeEquivalentTo([_m1, _m2]);
        page.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task ShouldFilterFromAnInstant_Inclusively()
    {
        AuthenticateAsMainAdmin();

        // Act
        var page = await ListAsync($"?from={Instant(T3)}");

        // Assert — the row at exactly T3 is in.
        Ids(page).Should().BeEquivalentTo([_m3, _m4, _m5]);
        page.TotalCount.Should().Be(3);
    }

    [Fact]
    public async Task ShouldFilterToAnInstant_Inclusively()
    {
        AuthenticateAsMainAdmin();

        // Act
        var page = await ListAsync($"?to={Instant(T3)}");

        // Assert — the row at exactly T3 is in.
        Ids(page).Should().BeEquivalentTo([_m1, _m2, _m3]);
        page.TotalCount.Should().Be(3);
    }

    [Fact]
    public async Task ShouldApplyEveryFilterTogether()
    {
        AuthenticateAsMainAdmin();

        // Act
        var page = await ListAsync($"?action={AuditAction.UserUpdated}&actorUserId={MainAdminId}&from={Instant(T2)}");

        // Assert — only M3 passes all three. Without the action M5 joins it, without the actor M2 does, and
        // without the lower bound M1 does, so a filter that was accepted and ignored cannot hide here.
        Ids(page).Should().Equal(_m3);
        page.TotalCount.Should().Be(1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?sort=newest")]
    public async Task ShouldReturnNewestFirst_WhenSortIsAbsentOrNewest(string query)
    {
        AuthenticateAsMainAdmin();

        // Act
        var page = await ListAsync(query);

        // Assert
        Ids(page).Should().Equal(_m5, _m4, _m3, _m2, _m1);
    }

    [Fact]
    public async Task ShouldReturnOldestFirst_WhenSortIsOldest()
    {
        AuthenticateAsMainAdmin();

        // Act
        var page = await ListAsync($"?sort={AuditLogSort.Oldest}");

        // Assert
        Ids(page).Should().Equal(_m1, _m2, _m3, _m4, _m5);
    }

    [Fact]
    public async Task ShouldApplyTheSortBeforePaging()
    {
        AuthenticateAsMainAdmin();

        // Act — the second page of two, oldest first.
        var page = await ListAsync($"?sort={AuditLogSort.Oldest}&page=2&pageSize=2");

        // Assert — the whole page, in order. With five rows the middle one is third from both ends, so a
        // single-row page would pass against a query that ignored the sort.
        Ids(page).Should().Equal(_m3, _m4);
        page.TotalCount.Should().Be(5);
    }

    [Fact]
    public async Task ShouldPageThroughTheCallersRows_WithAStableTotal()
    {
        AuthenticateAsMainAdmin();

        // Act
        var first = await ListAsync("?page=1&pageSize=2");
        var second = await ListAsync("?page=2&pageSize=2");
        var third = await ListAsync("?page=3&pageSize=2");
        var beyond = await ListAsync("?page=4&pageSize=2");

        // Assert — every row exactly once, and the total is the caller's total on every page, including the
        // one past the end.
        Ids(first).Should().Equal(_m5, _m4);
        Ids(second).Should().Equal(_m3, _m2);
        Ids(third).Should().Equal(_m1);
        beyond.Items.Should().BeEmpty();

        new[] { first, second, third, beyond }.Should().OnlyContain(x => x.TotalCount == 5 && x.PageSize == 2);
        new[] { first, second, third, beyond }.Select(x => x.Page).Should().Equal(1, 2, 3, 4);
    }

    [Theory]
    [MemberData(nameof(InvalidQueries))]
    public async Task ShouldRejectAnInvalidQuery_WithTheValidationFailedEnvelope(string query, string property)
    {
        AuthenticateAsMainAdmin();

        // Act
        var response = await Client.GetAsync($"{Route}?{query}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        root.GetProperty("error").GetString().Should().Be("validation_failed");
        root.GetProperty("errors")
            .EnumerateArray()
            .Should()
            .Contain(item => item.GetProperty("property").GetString() == property);
    }

    [Fact]
    public async Task ShouldAcceptTheLargestPageSize_TheDeletedUsersViewAsksFor()
    {
        AuthenticateAsMainAdmin();

        // Act — the request the LA "Deleted users" page sends. 200 is FieldLimits.MaxPageSize exactly, so
        // lowering the limit would turn that page into a 400.
        var response = await Client.GetAsync($"{Route}?action={AuditAction.UserDeleted}&pageSize=200");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await response.ToResponseModel<PagedAuditLogs<AuditLogListItemDto>>();
        page.PageSize.Should().Be(200);
        page.Items.Should().BeEmpty();
    }

    [Theory]
    [InlineData(Role.OrganisationAdmin)]
    [InlineData(Role.Lead)]
    public async Task ShouldReturnItsOwnRowById_WithSnapshotsAndDisplayNames(string role)
    {
        AuthenticateAs(role, Guid.NewGuid(), _mainOrg.Id.Value);

        // Act
        var response = await Client.GetAsync($"{Route}/{_m2}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = await response.ToResponseModel<AuditLogDetailDto>();

        detail.Id.Should().Be(_m2);
        detail.OccurredAt.Should().Be(T2);
        detail.Action.Should().Be(AuditAction.UserUpdated);
        detail.ActorUserId.Should().Be(MainLeadId);
        detail.ActorDisplayName.Should().Be("lead@main-council.gov.uk member");
        detail.TargetType.Should().Be(AuditTargetType.User);
        detail.TargetId.Should().Be(MainLeadId);
        detail.TargetDisplayName.Should().Be("lead@main-council.gov.uk member");
        detail.OrganisationId.Should().Be(_mainOrg.Id.Value);
        detail.OrganisationName.Should().Be(_mainOrg.Name);

        // jsonb re-spaces and may reorder what was stored, so the snapshots are compared as JSON.
        using var before = JsonDocument.Parse(detail.Before!);
        using var after = JsonDocument.Parse(detail.After!);
        before.RootElement.GetProperty("role").GetString().Should().Be(Role.OrganisationAdmin);
        after.RootElement.GetProperty("role").GetString().Should().Be(Role.Lead);
    }

    [Theory]
    [InlineData("another-organisation")]
    [InlineData("no-organisation")]
    [InlineData("unknown")]
    public async Task ShouldReturn404_ForARowOutsideTheCallersOrganisation(string which)
    {
        AuthenticateAsMainAdmin();

        // Arrange — a row that exists but is not this LA's must be indistinguishable from one that does not
        // exist, or the id becomes a way to learn that another LA did something.
        var id = which switch
        {
            "another-organisation" => _s1,
            "no-organisation" => _n1,
            _ => Guid.NewGuid(),
        };

        // Act
        var response = await Client.GetAsync($"{Route}/{id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().NotContain(_secondOrg.Name);
    }

    [Fact]
    public async Task ShouldEnrichListItems_WithDisplayNames()
    {
        AuthenticateAsMainAdmin();

        // Act
        var page = await ListAsync(string.Empty);

        // Assert
        var userRow = page.Items.Single(x => x.Id == _m1);
        userRow.ActorDisplayName.Should().Be("main organisation admin");
        userRow.TargetDisplayName.Should().Be("lead@main-council.gov.uk member");
        userRow.OrganisationName.Should().Be(_mainOrg.Name);

        // A target is named only when it is a User. M4's TargetId is a real user's id, and it is an Activity.
        var activityRow = page.Items.Single(x => x.Id == _m4);
        activityRow.TargetDisplayName.Should().BeNull();
    }

    public async Task InitializeAsync()
    {
        Context.AuditLogs.RemoveRange(Context.AuditLogs);
        await Context.Organisations.AddRangeAsync(_mainOrg, _secondOrg);
        await Context.OrganisationUsers.AddRangeAsync(_mainAdmin, _mainLead, _secondAdmin);
        await SaveChangesAsync();

        var main = _mainOrg.Id;
        var second = _secondOrg.Id;
        var admin = _mainAdmin.UserId;
        var lead = _mainLead.UserId;

        _m3 = await SeedAsync(main, T3, AuditAction.UserUpdated, admin, AuditTargetType.User, MainAdminId);
        _s1 = await SeedAsync(second, T2, AuditAction.UserUpdated, admin, AuditTargetType.User, MainLeadId);
        _m5 = await SeedAsync(main, T5, AuditAction.LocationCreated, admin, AuditTargetType.Location, Guid.NewGuid());
        _m1 = await SeedAsync(main, T1, AuditAction.UserUpdated, admin, AuditTargetType.User, MainLeadId);
        _n1 = await SeedAsync(null, T3, AuditAction.UserUpdated, admin, AuditTargetType.User, MainLeadId);
        _m4 = await SeedAsync(main, T4, AuditAction.ActivityCreated, lead, AuditTargetType.Activity, MainLeadId);
        _s2 = await SeedAsync(second, T4, AuditAction.ActivityCreated, _secondAdmin.UserId, AuditTargetType.Activity, Guid.NewGuid());
        _m2 = await SeedAsync(
            main,
            T2,
            AuditAction.UserUpdated,
            lead,
            AuditTargetType.User,
            MainLeadId,
            before: """{"role":"organisation admin"}""",
            after: """{"role":"lead"}""");
    }

    public async Task DisposeAsync()
    {
        Context.AuditLogs.RemoveRange(Context.AuditLogs);
        Context.OrganisationUsers.RemoveRange(Context.OrganisationUsers);
        Context.Users.RemoveRange(Context.Users);
        Context.Organisations.RemoveRange(Context.Organisations);
        await SaveChangesAsync();
        AuthenticateAsAdmin();
    }

    private static List<Guid> Ids(PagedAuditLogs<AuditLogListItemDto> page) => page.Items.Select(x => x.Id).ToList();

    // Round-trip format with the Z, so the API reads the instant as UTC. An offset-less value would be taken
    // as the server's local time and shift on any machine that is not on UTC.
    private static string Instant(DateTime utc) => Uri.EscapeDataString(utc.ToString("O"));

    // Called from the test body, never from an async helper: the identity is AsyncLocal, and one set inside
    // an awaited method does not flow back to its caller.
    private void AuthenticateAsMainAdmin() => AuthenticateAs(Role.OrganisationAdmin, MainAdminId, _mainOrg.Id.Value);

    private async Task<PagedAuditLogs<AuditLogListItemDto>> ListAsync(string query)
    {
        var response = await Client.GetAsync(Route + query);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return await response.ToResponseModel<PagedAuditLogs<AuditLogListItemDto>>();
    }

    // AuditLog.Create takes no time and the entity exposes no setter for it, so the instant goes in through
    // the change tracker — the same route AuditLogRepository.AppendAsync uses.
    private async Task<Guid> SeedAsync(
        OrganisationId? organisationId,
        DateTime occurredAt,
        string action,
        UserId actor,
        string targetType,
        Guid targetId,
        string? before = null,
        string? after = null)
    {
        var log = AuditLog.Create(actor, targetType, targetId, action, before, after, organisationId);
        var entry = await Context.AuditLogs.AddAsync(log);

        entry.Property(x => x.OccurredAt).CurrentValue = occurredAt;
        await SaveChangesAsync();

        return log.Id.Value;
    }
}