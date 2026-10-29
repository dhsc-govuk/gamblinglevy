using System.Net.Http.Json;
using Api.Dtos;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Application.Common.Settings;
using Domain.Collections;
using Domain.DataCollectionForms;
using Domain.Organisations;
using Domain.OrganisationUsers;
using Domain.Submissions;
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
/// BLUM-8155 — telling LA users of an organisation that did not submit that the reporting window has closed.
///
/// Owed the day after the window closed (BLUM-8399). The deadline is the last day LAs can submit, so a deadline
/// window closes at the end of it and the email is due on deadline + 1 — the same day CollectionClosureNotifier's
/// DHSC notice fires. A manual close happens during its day, so the email is due on the close date + 1. Driven through
/// ICollectionWindowClosedLaNotifier directly rather than through the hosted service that schedules it —
/// IntegrationTestWebFactory unhooks that worker for the suite (RemoveBackgroundServices).
///
/// The deadline is pushed into the past through the change tracker rather than by creating a collection with one:
/// the create validator refuses a past deadline, which is exactly right and exactly unhelpful here.
/// </summary>
public class CollectionWindowClosedLaNotificationTests : BaseIntegrationTest, IAsyncLifetime
{
    private const string Route = "data-collections";

    private static Organisation MainOrg => OrganisationsData.MainOrganisation;

    private static Organisation SecondOrg => OrganisationsData.SecondOrganisation;

    private readonly IntegrationTestWebFactory _factory;
    private readonly ApplicationSettings _settings;

    private Guid _formModuleId;
    private OrganisationUser _mainOrgUser = null!;
    private OrganisationUser _secondOrgUser = null!;

    public CollectionWindowClosedLaNotificationTests(IntegrationTestWebFactory factory)
        : base(factory)
    {
        _factory = factory;
        _settings = factory.Services.GetRequiredService<ApplicationSettings>();
    }

    private sealed record CollectionResult(Guid Id, string Name, string Status, bool IsDraft);

    [Fact]
    public async Task ShouldEmailOnlyOrganisationsThatHaveNotSubmitted_OnceTheWindowHasBeenClosedForADay()
    {
        const string name = "Outstanding only closed window";
        var id = await CreateOpenCollectionAsync(name);

        // SecondOrg is finished; MainOrg is not. AC (recipient: organisations that did not submit).
        await SeedSubmittedSubmissionAsync(id, SecondOrg);
        var deadline = await SetDeadlineAsync(id, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1));

        (await SweepAsync()).Should().Be(1);

        RecipientsFor(name).Should().BeEquivalentTo([_mainOrgUser.User!.Email]);
        RecipientsFor(name).Should().NotContain(_secondOrgUser.User!.Email);

        var sent = SentFor(name).Should().ContainSingle().Subject;
        sent.RecipientName.Should().Be(_mainOrgUser.User!.Name.ToString());
        sent.SubmissionDeadline.Should().Be(deadline);

        (await WindowClosedLaNotifiedAtAsync(id)).Should().NotBeNull();
    }

    [Fact]
    public async Task ShouldNotNotify_OnTheDeadlineDay()
    {
        const string name = "Deadline today window";
        var id = await CreateOpenCollectionAsync(name);

        // LAs can still submit all day on the deadline, so the window has not closed yet. BLUM-8399's tests below
        // (deadline yesterday → notified) pin the other side of the boundary.
        await SetDeadlineAsync(id, DateOnly.FromDateTime(DateTime.UtcNow));

        (await SweepAsync()).Should().Be(0);
        RecipientsFor(name).Should().BeEmpty();
        (await WindowClosedLaNotifiedAtAsync(id)).Should().BeNull();
    }

    [Fact]
    public async Task ShouldNotifyOnlyOnce_OnceTheWindowHasBeenClosedForADay()
    {
        const string name = "Fire once closed window";
        var id = await CreateOpenCollectionAsync(name);
        await SetDeadlineAsync(id, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1));

        (await SweepAsync()).Should().Be(1);
        var firstSweep = RecipientsFor(name);
        firstSweep.Should().NotBeEmpty();

        // The whole point of WindowClosedLaNotifiedAt: the sweep runs on a timer and at every cold start, so
        // without the marker every run would re-send. A single-sweep assertion passes against a version with no
        // marker.
        (await SweepAsync()).Should().Be(0);
        RecipientsFor(name).Should().BeEquivalentTo(firstSweep);
    }

    [Fact]
    public async Task ShouldNotNotifyAWindowThatClosedLongerAgoThanTheMaxAge()
    {
        const string name = "Long expired closed window";
        var id = await CreateOpenCollectionAsync(name);

        var maxAgeDays = _settings.CollectionNotifications.MaxWindowClosedLaNotificationAgeDays;
        await SetDeadlineAsync(id, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-(maxAgeDays + 1)));

        // The backstop behind the deploy-time backfill: anything that leaves historical windows with a null
        // marker — a restore from backup, a data reset — must not mail every outstanding LA once per window.
        (await SweepAsync()).Should().Be(0);
        RecipientsFor(name).Should().BeEmpty();
    }

    [Fact]
    public async Task ShouldStampAWindowWithNoOutstandingOrganisations_RatherThanRetryingForever()
    {
        const string name = "Nobody outstanding closed window";
        var id = await CreateOpenCollectionAsync(name);
        await SeedSubmittedSubmissionAsync(id, MainOrg);
        await SeedSubmittedSubmissionAsync(id, SecondOrg);
        await SetDeadlineAsync(id, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1));

        // "Nobody was outstanding" and "outstanding organisations exist but nothing could be sent" are different:
        // the first is finished and must not recur, the second is owed a retry.
        (await SweepAsync()).Should().Be(1);
        RecipientsFor(name).Should().BeEmpty();
        (await WindowClosedLaNotifiedAtAsync(id)).Should().NotBeNull();
    }

    [Fact]
    public async Task ShouldRetryOnTheNextSweep_WhenNotifyIsNotConfiguredYet()
    {
        const string name = "Unconfigured window closed window";
        var id = await CreateOpenCollectionAsync(name);
        await SetDeadlineAsync(id, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1));

        // The state this feature ships in: Notify.CollectionWindowClosedLaTemplateId is empty until DHSC creates
        // the template, so every send is refused before it is attempted. That is not a send that failed — nothing
        // was delivered and nothing could have been — so the marker must stay clear, or every window closing
        // before the template exists would be marked notified forever.
        InMemoryCollectionWindowClosedLaEmailService.NotConfiguredFor(name);

        (await SweepAsync()).Should().Be(0);
        RecipientsFor(name).Should().BeEmpty();
        (await WindowClosedLaNotifiedAtAsync(id)).Should().BeNull();

        // So the backlog goes out once Notify__CollectionWindowClosedLaTemplateId is set.
        InMemoryCollectionWindowClosedLaEmailService.ConfigureFor(name);

        (await SweepAsync()).Should().Be(1);
        RecipientsFor(name).Should().NotBeEmpty();
    }

    [Fact]
    public async Task ShouldStillMarkTheWindowNotified_WhenTheNotificationFails()
    {
        const string name = "Failing window closed window";
        var id = await CreateOpenCollectionAsync(name);
        await SetDeadlineAsync(id, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1));

        InMemoryCollectionWindowClosedLaEmailService.FailFor(name);

        // NotifyEmailServiceBase bounds how long it WAITS but cannot abort the HTTP call the Notify SDK has
        // started, so a degraded-but-working Notify returns a failure for mail that really did arrive. Retrying
        // that would mail every outstanding LA twice, which is worse than the missed mail the log already records.
        (await SweepAsync()).Should().Be(1);
        RecipientsFor(name).Should().BeEmpty();
        (await WindowClosedLaNotifiedAtAsync(id)).Should().NotBeNull();

        InMemoryCollectionWindowClosedLaEmailService.StopFailingFor(name);

        (await SweepAsync()).Should().Be(0);
        RecipientsFor(name).Should().BeEmpty();
    }

    [Fact]
    public async Task ShouldOweTheNotificationAgain_AfterTheCollectionIsReopened()
    {
        const string name = "Reopened closed window";
        var id = await CreateOpenCollectionAsync(name);
        await SetDeadlineAsync(id, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1));

        (await SweepAsync()).Should().Be(1);
        (await WindowClosedLaNotifiedAtAsync(id)).Should().NotBeNull();

        // RevertToDraftCollectionCommand only accepts an Open collection over HTTP (RevertToDraftCollectionCommandHandler
        // refuses a Closed one with CollectionInvalidTransitionException), so a closed one is exercised through the
        // aggregate directly — the point here is Collection.RevertToDraft()'s WindowClosedLaNotifiedAt reset, not
        // the command's status guard.
        Context.ChangeTracker.Clear();
        var collection = await Context.Collections.SingleAsync(c => c.Id == new CollectionId(id));
        collection.RevertToDraft(AdminsData.MainAdmin.Id, DateTime.UtcNow);
        await SaveChangesAsync();

        (await WindowClosedLaNotifiedAtAsync(id)).Should().BeNull();
    }

    [Fact]
    public async Task ShouldEmailEveryAssignedLa_WhenTheWindowIsSubmittedByAllLas()
    {
        const string name = "All LAs closed window";
        var id = await CreateOpenCollectionAsync(name);
        await SetDeadlineAsync(id, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1));

        (await SweepAsync()).Should().Be(1);

        // An all-LAs window carries NO collection_local_authorities rows. Resolution goes through
        // SubmissionAssignment via OutstandingSubmissionResolver, which also excludes VCSEs from the implicit case.
        RecipientsFor(name).Should().BeEquivalentTo([_mainOrgUser.User!.Email, _secondOrgUser.User!.Email]);
    }

    [Fact]
    public async Task ShouldNotifyForAnEarlyManualClose_UsingTheCloseDateRatherThanTheOriginalDeadline()
    {
        const string name = "Early manual close window";

        // The deadline is 25 days in the future (BaseRequest) — if the notifier judged this against
        // SubmissionDeadline alone, LAs would not be told for weeks, which is exactly what the code review on this
        // PR flagged.
        var id = await CreateOpenCollectionAsync(name);
        (await Client.PostAsync($"{Route}/{id}/close", null)).IsSuccessStatusCode.Should().BeTrue();

        // Close() just stamped ManuallyClosedAt = UtcNow; push it back to the last minute of yesterday — the
        // latest close that is owed today. Proves the notification fires off the actual close date, not the
        // far-future original deadline, and (BLUM-8399) that a close made late yesterday is not held back a
        // further day: the prefilter used to compare against midnight at the START of yesterday.
        var startOfToday = DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc);
        await SetManuallyClosedAtAsync(id, startOfToday.AddMinutes(-1));

        (await SweepAsync()).Should().Be(1);
        RecipientsFor(name).Should().BeEquivalentTo([_mainOrgUser.User!.Email, _secondOrgUser.User!.Email]);
    }

    [Fact]
    public async Task ShouldNotNotify_OnTheDayOfAManualClose()
    {
        const string name = "Just manually closed window";
        var id = await CreateOpenCollectionAsync(name);

        // Closed moments ago (Close()'s own ManuallyClosedAt = UtcNow, left untouched) — not yet a full day closed.
        (await Client.PostAsync($"{Route}/{id}/close", null)).IsSuccessStatusCode.Should().BeTrue();

        (await SweepAsync()).Should().Be(0);
        RecipientsFor(name).Should().BeEmpty();
        (await WindowClosedLaNotifiedAtAsync(id)).Should().BeNull();
    }

    [Fact]
    public async Task ShouldClaimOnlyOnce_WhenTwoInstancesRaceToNotifyTheSameCollection()
    {
        const string name = "Race claim window";
        var id = await CreateOpenCollectionAsync(name);
        await SetDeadlineAsync(id, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1));

        await using var scope = _factory.Services.CreateAsyncScope();
        var collectionRepository = scope.ServiceProvider.GetRequiredService<ICollectionRepository>();
        var collectionId = new CollectionId(id);

        // Only the first of two racing claims may win — a send-then-stamp ordering would let both instances
        // decide the collection is still owed a send and fan out to every outstanding contact twice, which is
        // what the code review on this PR flagged.
        (await collectionRepository.TryClaimWindowClosedLaNotificationAsync(collectionId, DateTime.UtcNow)).Should().BeTrue();
        (await collectionRepository.TryClaimWindowClosedLaNotificationAsync(collectionId, DateTime.UtcNow)).Should().BeFalse();
    }

    [Fact]
    public async Task ShouldAllowReclaiming_AfterTheClaimIsReleased()
    {
        const string name = "Release claim window";
        var id = await CreateOpenCollectionAsync(name);
        await SetDeadlineAsync(id, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1));

        await using var scope = _factory.Services.CreateAsyncScope();
        var collectionRepository = scope.ServiceProvider.GetRequiredService<ICollectionRepository>();
        var collectionId = new CollectionId(id);

        (await collectionRepository.TryClaimWindowClosedLaNotificationAsync(collectionId, DateTime.UtcNow)).Should().BeTrue();
        await collectionRepository.ReleaseWindowClosedLaNotificationClaimAsync(collectionId);

        // Same reasoning as CollectionWindowClosedLaNotifier releasing the claim when Notify has nothing to send
        // WITH: a released claim must be reclaimable, or the backlog would never go out once the template exists.
        (await collectionRepository.TryClaimWindowClosedLaNotificationAsync(collectionId, DateTime.UtcNow)).Should().BeTrue();
    }

    private async Task<int> SweepAsync()
    {
        // CreateAsyncScope, not CreateScope: RepositoryBase implements IAsyncDisposable only, and the sync
        // Dispose of a scope holding one throws.
        await using var scope = _factory.Services.CreateAsyncScope();
        var notifier = scope.ServiceProvider.GetRequiredService<ICollectionWindowClosedLaNotifier>();

        return await notifier.SweepAsync();
    }

    private static IReadOnlyList<string> RecipientsFor(string collectionName) =>
        InMemoryCollectionWindowClosedLaEmailService.Sent
            .Where(s => s.CollectionName == collectionName)
            .Select(s => s.Email)
            .ToList();

    private static IEnumerable<(string Email, string CollectionName, string RecipientName, DateOnly SubmissionDeadline)> SentFor(string collectionName) =>
        InMemoryCollectionWindowClosedLaEmailService.Sent.Where(s => s.CollectionName == collectionName);

    private async Task<DateTime?> WindowClosedLaNotifiedAtAsync(Guid id)
    {
        Context.ChangeTracker.Clear();

        return (await Context.Collections.AsNoTracking().SingleAsync(c => c.Id == new CollectionId(id)))
            .WindowClosedLaNotifiedAt;
    }

    // Moves the deadline backwards and returns it. SubmissionDeadline has a private setter and no domain method
    // moves it backwards — UpdateDetails refuses once the collection is Closed — so the change tracker is the
    // honest way in, same as CollectionClosureNotificationTests/CollectionReminderNotificationTests.
    private async Task<DateOnly> SetDeadlineAsync(Guid id, DateOnly deadline)
    {
        var collection = await Context.Collections.SingleAsync(c => c.Id == new CollectionId(id));
        Context.Entry(collection).Property(c => c.SubmissionDeadline).CurrentValue = deadline;
        await SaveChangesAsync();

        return deadline;
    }

    // Moves ManuallyClosedAt backwards, the way SetDeadlineAsync moves SubmissionDeadline backwards — Close()
    // always stamps UtcNow, and nothing in the domain moves it, so the change tracker is the only way in.
    private async Task SetManuallyClosedAtAsync(Guid id, DateTime manuallyClosedAt)
    {
        var collection = await Context.Collections.SingleAsync(c => c.Id == new CollectionId(id));
        Context.Entry(collection).Property(c => c.ManuallyClosedAt).CurrentValue = manuallyClosedAt;
        await SaveChangesAsync();
    }

    // Seeds a finalised (SubmittedAt set) submission directly — enough to make an organisation "has submitted"
    // without driving the whole multi-section submit flow. Mirrors
    // CollectionReminderNotificationTests.SeedSubmittedSubmissionAsync.
    private async Task SeedSubmittedSubmissionAsync(Guid collectionId, Organisation organisation)
    {
        var submitter = User.New(
            UserId.New(),
            new Name("Closed", "Submitter"),
            $"closed.submitter.{Guid.NewGuid():N}@example.gov.uk",
            new SubId(Guid.NewGuid()),
            UserStatus.Active,
            UserRole.Lead,
            DateTime.UtcNow);
        await Context.Users.AddAsync(submitter);

        var submission = Submission.Start(
            SubmissionId.New(),
            new CollectionId(collectionId),
            organisation.Id,
            Array.Empty<(FormModuleId ModuleId, string ModuleCode)>(),
            submitter.Id,
            DateTime.UtcNow);
        submission.Submit($"REF-{Guid.NewGuid():N}"[..12], submitter.Id, DateTime.UtcNow);
        await Context.Submissions.AddAsync(submission);

        await SaveChangesAsync();
    }

    private async Task<Guid> CreateOpenCollectionAsync(string name) => await CreateAsync(BaseRequest(name));

    private async Task<Guid> CreateAsync(CreateCollectionRequestDto request)
    {
        var response = await Client.PostAsJsonAsync(Route, request);
        response.IsSuccessStatusCode.Should().BeTrue();

        return (await response.ToResponseModel<CollectionResult>()).Id;
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
            OpenDate: today.AddDays(-15),
            Deadline: today.AddDays(25),
            IsSubmittedByAllLas: true,
            SaveAsDraft: false,
            LocalAuthorityIds: null,
            FormModuleIds: new[] { _formModuleId },
            Reminders: null);
    }

    public async Task InitializeAsync()
    {
        foreach (var organisation in new[] { MainOrg, SecondOrg })
        {
            if (!await Context.Organisations.AsNoTracking().AnyAsync(o => o.Id == organisation.Id))
            {
                await Context.Organisations.AddAsync(organisation);
            }
        }

        await SaveChangesAsync();

        _mainOrgUser = await EnsureOrganisationUserAsync(MainOrg, "windowclosed.main@org.com");
        _secondOrgUser = await EnsureOrganisationUserAsync(SecondOrg, "windowclosed.second@org.com");

        Context.Submissions.RemoveRange(Context.Submissions);
        Context.Collections.RemoveRange(Context.Collections);
        Context.AuditLogs.RemoveRange(Context.AuditLogs);
        await SaveChangesAsync();

        _formModuleId = (await Context.FormModules.AsNoTracking().FirstAsync()).Id.Value;

        InMemoryCollectionWindowClosedLaEmailService.Clear();
        AuthenticateAsAdmin();
    }

    private async Task<OrganisationUser> EnsureOrganisationUserAsync(Organisation organisation, string email)
    {
        Context.ChangeTracker.Clear();

        var existing = await Context.OrganisationUsers
            .AsNoTracking()
            .Include(x => x.User)
            .FirstOrDefaultAsync(x => x.User!.Email == email);

        if (existing is not null)
        {
            return existing;
        }

        var user = User.New(
            id: UserId.New(),
            name: new Name("Closed", "Contact"),
            email: email,
            subId: new SubId(Guid.NewGuid()),
            status: UserStatus.Active,
            role: UserRole.OrganisationAdmin,
            utcNow: DateTime.UtcNow);

        var organisationUser = OrganisationUser.New(
            id: new OrganisationUserId(user.Id.Value),
            userId: user.Id,
            organisationId: organisation.Id,
            user: user);

        await Context.Users.AddAsync(user);
        await Context.OrganisationUsers.AddAsync(organisationUser);
        await SaveChangesAsync();

        organisationUser = await Context.OrganisationUsers
            .AsNoTracking()
            .Include(x => x.User)
            .FirstAsync(x => x.Id == organisationUser.Id);

        return organisationUser;
    }

    public async Task DisposeAsync()
    {
        InMemoryCollectionWindowClosedLaEmailService.Clear();

        Context.Submissions.RemoveRange(Context.Submissions);
        Context.Collections.RemoveRange(Context.Collections);
        Context.AuditLogs.RemoveRange(Context.AuditLogs);
        await SaveChangesAsync();

        AuthenticateAsAdmin();
    }
}