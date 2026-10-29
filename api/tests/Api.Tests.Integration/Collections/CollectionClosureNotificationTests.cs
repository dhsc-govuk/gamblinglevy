using System.Net.Http.Json;
using Api.Dtos;
using Application.Common.Interfaces;
using Application.Common.Settings;
using Domain.Collections;
using Domain.Users;
using Domain.ValueObjects;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tests.Common;
using Tests.Common.Services;
using Tests.Data;
using Xunit;

namespace Api.Tests.Integration.Collections;

/// <summary>
/// BLUM-7719 — telling DHSC Super Admins that a reporting window has closed.
///
/// A window closes down two paths and only one of them is a request. CloseCollectionCommand sets
/// IsManuallyClosed; far more often the submission deadline simply passes, where nothing runs at all and
/// Collection.GetStatus() just starts answering Closed. Both are covered here, and both go through
/// ICollectionClosureNotifier — the sweep directly rather than through the hosted service that schedules it, since
/// IntegrationTestWebFactory unhooks that worker for the suite (RemoveBackgroundServices) and waiting on a
/// PeriodicTimer would be slow and flaky for no extra coverage.
///
/// The deadline is pushed into the past through the change tracker rather than by creating a collection with one:
/// the create validator refuses a past deadline, which is exactly right and exactly unhelpful here.
/// </summary>
public class CollectionClosureNotificationTests : BaseIntegrationTest, IAsyncLifetime
{
    private const string Route = "data-collections";

    private static readonly User LaLead = User.New(
        id: UserId.New(),
        name: new Name("La", "Lead"),
        email: "closure.la.lead@email.com",
        subId: new SubId(Guid.NewGuid()),
        status: UserStatus.Active,
        role: UserRole.Lead,
        utcNow: DateTime.UtcNow);

    private readonly IntegrationTestWebFactory _factory;
    private readonly ApplicationSettings _settings;

    private Guid _formModuleId;

    public CollectionClosureNotificationTests(IntegrationTestWebFactory factory)
        : base(factory)
    {
        _factory = factory;
        _settings = factory.Services.GetRequiredService<ApplicationSettings>();
    }

    private sealed record CollectionResult(Guid Id, string Name, string Status, bool IsDraft);

    [Fact]
    public async Task ShouldNotifyEveryActiveDhscAdmin_WhenAWindowIsClosedByHand()
    {
        const string name = "Manually closed window";
        var id = await CreateOpenCollectionAsync(name);

        var response = await Client.PostAsync($"{Route}/{id}/close", null);

        response.IsSuccessStatusCode.Should().BeTrue();
        (await response.ToResponseModel<CollectionResult>()).Status.Should().Be("closed");

        // Compared against the database rather than a hardcoded pair — the app seeds a default admin at boot, so
        // "every active admin" is more than the users this class adds.
        RecipientsFor(name).Should().BeEquivalentTo(await ActiveAdminEmailsAsync());

        (await ClosureNotifiedAtAsync(id)).Should().NotBeNull();
    }

    [Fact]
    public async Task ShouldNotNotifyDeactivatedAdminsOrLaUsers()
    {
        const string name = "Recipient scoping window";
        var id = await CreateOpenCollectionAsync(name);

        await Client.PostAsync($"{Route}/{id}/close", null);

        // Deactivated admins are excluded because IUserRepository.GetActiveAdminsAsync filters on status; LA users
        // because it filters on role. Both are in the database for this class, so neither exclusion is vacuous.
        RecipientsFor(name).Should().NotContain(AdminsData.DeactivatedAdmin.Email);
        RecipientsFor(name).Should().NotContain(LaLead.Email);
    }

    [Fact]
    public async Task ShouldNotifyAWindowThatClosedOnItsOwn_AndOnlyOnce()
    {
        const string name = "Expired window";
        var id = await CreateOpenCollectionAsync(name);
        await SetDeadlineAsync(id, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1));

        (await SweepAsync()).Should().Be(1);
        var firstSweep = RecipientsFor(name);
        firstSweep.Should().BeEquivalentTo(await ActiveAdminEmailsAsync());

        // The whole point of ClosureNotifiedAt: the sweep runs on a timer and at every cold start, so without the
        // marker every run would re-send. A single-sweep assertion passes against a version that has no marker.
        (await SweepAsync()).Should().Be(0);
        RecipientsFor(name).Should().BeEquivalentTo(firstSweep);
    }

    [Fact]
    public async Task ShouldNotNotifyWindowsThatAreNotClosed()
    {
        var draft = await CreateAsync(DraftRequest("Draft window"));
        var open = await CreateAsync(OpenRequest("Still open window"));
        var scheduled = await CreateAsync(ScheduledRequest("Scheduled window"));

        var deleted = await CreateOpenCollectionAsync("Deleted expired window");
        await SetDeadlineAsync(deleted, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1));
        (await Client.DeleteAsync($"{Route}/{deleted}")).IsSuccessStatusCode.Should().BeTrue();

        (await SweepAsync()).Should().Be(0);

        RecipientsFor("Draft window").Should().BeEmpty();
        RecipientsFor("Still open window").Should().BeEmpty();
        RecipientsFor("Scheduled window").Should().BeEmpty();
        RecipientsFor("Deleted expired window").Should().BeEmpty();

        (await ClosureNotifiedAtAsync(draft)).Should().BeNull();
        (await ClosureNotifiedAtAsync(open)).Should().BeNull();
        (await ClosureNotifiedAtAsync(scheduled)).Should().BeNull();
    }

    [Fact]
    public async Task ShouldNotNotifyAWindowThatClosedLongerAgoThanTheMaxAge()
    {
        const string name = "Long expired window";
        var id = await CreateOpenCollectionAsync(name);

        var maxAgeDays = _settings.CollectionNotifications.MaxClosureNotificationAgeDays;
        await SetDeadlineAsync(id, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-(maxAgeDays + 1)));

        // The backstop behind the deploy-time backfill: anything that leaves historical windows with a null marker
        // — a restore from backup, a data reset — must not mail every admin once per window.
        (await SweepAsync()).Should().Be(0);
        RecipientsFor(name).Should().BeEmpty();
    }

    [Fact]
    public async Task ShouldStillCloseTheWindow_WhenTheNotificationFails()
    {
        const string name = "Failing notification window";
        var id = await CreateOpenCollectionAsync(name);
        InMemoryCollectionClosedEmailService.FailFor(name);

        var response = await Client.PostAsync($"{Route}/{id}/close", null);

        // The close is committed and audited before the notification is attempted, so a Notify outage must not
        // turn a successful close into a 500.
        response.IsSuccessStatusCode.Should().BeTrue();
        (await response.ToResponseModel<CollectionResult>()).Status.Should().Be("closed");
        RecipientsFor(name).Should().BeEmpty();

        // Stamped even though nothing was delivered, and this is the point of the test rather than an accident of
        // it: NotifyEmailServiceBase bounds how long it WAITS but cannot abort the HTTP call the Notify SDK has
        // started, so a degraded-but-working Notify returns a failure for mail that really did arrive. Retrying
        // that would mail every DHSC admin twice, which is worse than the missed mail the log already records.
        (await ClosureNotifiedAtAsync(id)).Should().NotBeNull();

        InMemoryCollectionClosedEmailService.StopFailingFor(name);

        (await SweepAsync()).Should().Be(0);
        RecipientsFor(name).Should().BeEmpty();
    }

    [Fact]
    public async Task ShouldRetryOnTheNextSweep_WhenNotifyIsNotConfiguredYet()
    {
        const string name = "Unconfigured Notify window";
        var id = await CreateOpenCollectionAsync(name);

        // The state this feature actually ships in: Notify.CollectionClosedTemplateId is empty until DHSC creates
        // the template, so every send is refused before it is attempted. That is not a send that failed — nothing
        // was delivered and nothing could have been — so the marker must stay clear, or every window closing
        // before the template exists would be marked notified forever.
        InMemoryCollectionClosedEmailService.NotConfiguredFor(name);

        var response = await Client.PostAsync($"{Route}/{id}/close", null);

        response.IsSuccessStatusCode.Should().BeTrue();
        (await response.ToResponseModel<CollectionResult>()).Status.Should().Be("closed");
        (await ClosureNotifiedAtAsync(id)).Should().BeNull();
        RecipientsFor(name).Should().BeEmpty();

        // So the backlog goes out once Notify__CollectionClosedTemplateId is set.
        InMemoryCollectionClosedEmailService.ConfigureFor(name);

        (await SweepAsync()).Should().Be(1);
        RecipientsFor(name).Should().BeEquivalentTo(await ActiveAdminEmailsAsync());
    }

    [Fact]
    public async Task ShouldStampAWindowWithNoRecipients_RatherThanRetryingForever()
    {
        const string name = "No recipients window";
        var id = await CreateOpenCollectionAsync(name);

        var deactivated = await Context.Users
            .Where(u => u.Role == UserRole.Admin && u.Status == UserStatus.Active)
            .ToListAsync();
        foreach (var admin in deactivated)
        {
            admin.Deactivate(DateTime.UtcNow);
        }

        await SaveChangesAsync();

        var deactivatedIds = deactivated.Select(u => u.Id).ToList();

        try
        {
            await SetDeadlineAsync(id, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1));

            // "Nobody was reachable" and "there was nobody to reach" are different: the first is owed a retry,
            // the second is finished. Without the distinction a service with no active admins would re-scan every
            // closed window on every sweep.
            (await SweepAsync()).Should().Be(1);
            RecipientsFor(name).Should().BeEmpty();
            (await ClosureNotifiedAtAsync(id)).Should().NotBeNull();
        }
        finally
        {
            // Only the rows this test switched off. Reactivating every admin would also reactivate
            // AdminsData.DeactivatedAdmin, which InitializeAsync seeds only when it is absent and therefore never
            // restores — and the factory is an IClassFixture, so the database outlives this method. The next test
            // in the class to assert that deactivated admins are excluded would then fail, depending on the order
            // xUnit happened to pick.
            Context.ChangeTracker.Clear();
            foreach (var admin in await Context.Users.Where(u => deactivatedIds.Contains(u.Id)).ToListAsync())
            {
                admin.Reactivate(DateTime.UtcNow);
            }

            await SaveChangesAsync();
        }
    }

    private async Task<int> SweepAsync()
    {
        // CreateAsyncScope, not CreateScope: RepositoryBase implements IAsyncDisposable only, and the sync
        // Dispose of a scope holding one throws.
        await using var scope = _factory.Services.CreateAsyncScope();
        var notifier = scope.ServiceProvider.GetRequiredService<ICollectionClosureNotifier>();

        return await notifier.SweepAsync();
    }

    private async Task<IReadOnlyList<string>> ActiveAdminEmailsAsync()
    {
        return await Context.Users
            .AsNoTracking()
            .Where(u => u.Role == UserRole.Admin && u.Status == UserStatus.Active)
            .Select(u => u.Email)
            .ToListAsync();
    }

    private static IReadOnlyList<string> RecipientsFor(string collectionName) =>
        InMemoryCollectionClosedEmailService.Sent
            .Where(s => s.CollectionName == collectionName)
            .Select(s => s.Email)
            .ToList();

    private async Task<DateTime?> ClosureNotifiedAtAsync(Guid id)
    {
        Context.ChangeTracker.Clear();

        return (await Context.Collections.AsNoTracking().SingleAsync(c => c.Id == new CollectionId(id)))
            .ClosureNotifiedAt;
    }

    private async Task SetDeadlineAsync(Guid id, DateOnly deadline)
    {
        // ClosureNotifiedAt aside, SubmissionDeadline has a private setter and no domain method moves it backwards
        // — UpdateDetails refuses once the collection is Closed. The change tracker is the honest way in.
        var collection = await Context.Collections.SingleAsync(c => c.Id == new CollectionId(id));
        Context.Entry(collection).Property(c => c.SubmissionDeadline).CurrentValue = deadline;
        await SaveChangesAsync();
    }

    private async Task<Guid> CreateOpenCollectionAsync(string name) => await CreateAsync(OpenRequest(name));

    private async Task<Guid> CreateAsync(CreateCollectionRequestDto request)
    {
        var response = await Client.PostAsJsonAsync(Route, request);
        response.IsSuccessStatusCode.Should().BeTrue();

        return (await response.ToResponseModel<CollectionResult>()).Id;
    }

    private CreateCollectionRequestDto DraftRequest(string name) => BaseRequest(name) with
    {
        OpenDate = null,
        Deadline = null,
        SaveAsDraft = true,
    };

    private CreateCollectionRequestDto OpenRequest(string name) => BaseRequest(name);

    private CreateCollectionRequestDto ScheduledRequest(string name)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        return BaseRequest(name) with { OpenDate = today.AddDays(5), Deadline = today.AddDays(25) };
    }

    private CreateCollectionRequestDto BaseRequest(string name)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        return new CreateCollectionRequestDto(
            Name: name,
            Purpose: null,
            Type: "quarterly_report",
            FinancialYear: "2026/27",
            Quarter: 1,
            WindowSameAsReportingPeriod: false,
            OpenDate: today.AddDays(-5),
            Deadline: today.AddDays(25),
            IsSubmittedByAllLas: true,
            SaveAsDraft: false,
            LocalAuthorityIds: null,
            FormModuleIds: new[] { _formModuleId },
            Reminders: null);
    }

    public async Task InitializeAsync()
    {
        // The authenticated admin must exist as a real users row — handlers persist it as the audit actor — and
        // the other three exist so the recipient filtering in GetActiveAdminsAsync is actually exercised.
        foreach (var user in new[] { AdminsData.MainAdmin, AdminsData.SecondAdmin, AdminsData.DeactivatedAdmin, LaLead })
        {
            if (!await Context.Users.AsNoTracking().AnyAsync(u => u.Id == user.Id))
            {
                await Context.Users.AddAsync(user);
            }
        }

        await SaveChangesAsync();

        Context.Collections.RemoveRange(Context.Collections);
        await SaveChangesAsync();

        _formModuleId = (await Context.FormModules.AsNoTracking().FirstAsync()).Id.Value;

        InMemoryCollectionClosedEmailService.Clear();
        AuthenticateAsAdmin();
    }

    public async Task DisposeAsync()
    {
        InMemoryCollectionClosedEmailService.Clear();
        Context.Collections.RemoveRange(Context.Collections);
        await SaveChangesAsync();

        AuthenticateAsAdmin();
    }
}