using System.Net.Http.Json;
using Api.Dtos;
using Application.Common.Interfaces;
using Application.Common.Settings;
using Application.MaturityAssessments.Protocol;
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
/// BLUM-7721 — announcing that a reporting window has opened: one email to every contact at every organisation
/// assigned to the collection, and a summary to every DHSC Super Admin.
///
/// A class of its own rather than more facts on CollectionReminderNotificationTests, because the two need
/// opposite fixtures. That class deliberately opens its windows OUTSIDE the catch-up floor so the announcement
/// never fires under its feet; every window here opens inside it. Folding them together would mean a flag on
/// every helper.
///
/// Both passes share CollectionReminderNotifier and one sweep, so this drives ICollectionReminderNotifier
/// directly — IntegrationTestWebFactory unhooks the hosted service that schedules it for the whole suite, and
/// waiting on a PeriodicTimer would be slow and flaky for no extra coverage.
/// </summary>
public class CollectionWindowOpenedNotificationTests : BaseIntegrationTest, IAsyncLifetime
{
    private const string Route = "data-collections";

    private static readonly Organisation SuspendedOrganisation = Organisation.New(
        id: OrganisationId.New(),
        name: "Window Opened Suspended Council",
        onsCode: "E06000913",
        region: Region.SouthWest,
        type: OrgType.LocalAuthority,
        eCode: "E1000913",
        onboardingStatus: OnboardingStatus.Suspended,
        utcNow: DateTime.UtcNow);

    private static readonly Organisation VcseOrganisation = Organisation.New(
        id: OrganisationId.New(),
        name: "Window Opened VCSE",
        onsCode: null,
        region: Region.London,
        type: OrgType.Vcse,
        eCode: "E1000914",
        onboardingStatus: OnboardingStatus.Active,
        utcNow: DateTime.UtcNow);

    private static readonly User LaLead = User.New(
        id: UserId.New(),
        name: new Name("WindowOpened", "Lead"),
        email: "window.opened.la.lead@email.com",
        subId: new SubId(Guid.NewGuid()),
        status: UserStatus.Active,
        role: UserRole.Lead,
        utcNow: DateTime.UtcNow);

    private static readonly User DeactivatedOrgUser = User.New(
        id: UserId.New(),
        name: new Name("WindowOpened", "Deactivated"),
        email: "window.opened.deactivated@org.com",
        subId: new SubId(Guid.NewGuid()),
        status: UserStatus.Active,
        role: UserRole.OrganisationAdmin,
        utcNow: DateTime.UtcNow);

    private readonly IntegrationTestWebFactory _factory;
    private readonly ApplicationSettings _settings;

    private Guid _formModuleId;
    private OrganisationUser _mainOrgUser = null!;
    private OrganisationUser _secondOrgUser = null!;

    public CollectionWindowOpenedNotificationTests(IntegrationTestWebFactory factory)
        : base(factory)
    {
        _factory = factory;
        _settings = factory.Services.GetRequiredService<ApplicationSettings>();
    }

    private static Organisation MainOrg => OrganisationsData.MainOrganisation;

    private static Organisation SecondOrg => OrganisationsData.SecondOrganisation;

    private sealed record CollectionResult(Guid Id, string Name, string Status, bool IsDraft);

    [Fact]
    public async Task ShouldEmailEveryContactAtEveryAssignedOrganisation()
    {
        const string name = "Window opened all LAs";
        var id = await CreateAsync(BaseRequest(name));

        // SecondOrg has already finished. It is STILL told the window is open — the deliberate difference from
        // the deadline reminder, which exists to chase people who have not submitted. AC1 says "each organisation
        // in the collection", full stop. (Note the tempting justification "nobody has submitted at window open"
        // is false: the catch-up floor lets this fire days late.)
        await SeedSubmittedSubmissionAsync(id, SecondOrg);

        (await SweepAsync()).Should().Be(1);

        WindowOpenedRecipientsFor(name).Should()
            .BeEquivalentTo([_mainOrgUser.User!.Email, _secondOrgUser.User!.Email]);
    }

    [Fact]
    public async Task ShouldEmailTheAssignedLas_WhenTheWindowNamesThemExplicitly()
    {
        const string name = "Window opened explicit LAs";

        // The explicit branch of SubmissionAssignment reads collection.LocalAuthorities, so the sweep's query has
        // to load them — and a missing Include fails silently: no recipients, and the trigger point consumed
        // anyway.
        var id = await CreateAsync(BaseRequest(name) with
        {
            IsSubmittedByAllLas = false,
            LocalAuthorityIds = new[] { MainOrg.Id.Value },
        });

        (await SweepAsync()).Should().Be(1);

        WindowOpenedRecipientsFor(name).Should().BeEquivalentTo([_mainOrgUser.User!.Email]);

        InMemoryCollectionWindowOpenedSummaryEmailService.Sent
            .Where(s => s.CollectionName == name)
            .Should().OnlyContain(s => s.AssignedCount == 1);
    }

    [Fact]
    public async Task ShouldEmailEveryActiveDhscAdmin()
    {
        const string name = "Window opened DHSC summary";
        await CreateAsync(BaseRequest(name));

        (await SweepAsync()).Should().Be(1);

        // AC2. Compared against the database rather than a literal, because the app seeds a default admin at boot
        // and "every active admin" is more than the users this class adds.
        SummaryRecipientsFor(name).Should().BeEquivalentTo(await ActiveAdminEmailsAsync());
        SummaryRecipientsFor(name).Should().NotContain(AdminsData.DeactivatedAdmin.Email);
        SummaryRecipientsFor(name).Should().NotContain(LaLead.Email);
    }

    [Fact]
    public async Task ShouldStateTheQuarterReportingPeriodAndDeadline()
    {
        const string name = "Window opened content";
        await CreateAsync(BaseRequest(name));

        await SweepAsync();

        // AC3 and AC4 ask for the same five facts with different copy around them, so both halves are asserted.
        // The reporting period is what the report COVERS; the deadline is when the window shuts. They are
        // different dates whenever WindowSameAsReportingPeriod is false, as it is here.
        var notice = InMemoryCollectionWindowOpenedEmailService.Sent
            .Where(s => s.CollectionName == name)
            .Should().NotBeEmpty().And.Subject.First();

        notice.PeriodLabel.Should().Be("Quarter 1 2026/27");
        notice.ReportingPeriodStart.Should().Be(new DateOnly(2026, 4, 1));
        notice.ReportingPeriodEnd.Should().Be(new DateOnly(2026, 6, 30));
        notice.SubmissionDeadline.Should().Be(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(25));
        notice.RecipientName.Should().NotBeNullOrWhiteSpace();

        var summary = InMemoryCollectionWindowOpenedSummaryEmailService.Sent
            .Where(s => s.CollectionName == name)
            .Should().NotBeEmpty().And.Subject.First();

        summary.PeriodLabel.Should().Be("Quarter 1 2026/27");
        summary.ReportingPeriodStart.Should().Be(new DateOnly(2026, 4, 1));
        summary.ReportingPeriodEnd.Should().Be(new DateOnly(2026, 6, 30));
        summary.SubmissionDeadline.Should().Be(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(25));
        summary.RecipientName.Should().NotBeNullOrWhiteSpace();
        summary.IsMaturityAssessment.Should().BeFalse();
    }

    [Fact]
    public async Task ShouldStateTheMaturityAssessmentPeriod_ForANonQuarterlyCollection()
    {
        const string name = "Window opened maturity assessment";

        // AC4's parenthetical, end to end. ValidateWindow forces Quarter to null for this type.
        await CreateAsync(BaseRequest(name) with { Type = "maturity_assessment", Quarter = null, FormModuleIds = MaturityCollectionForms.Ids });

        await SweepAsync();

        InMemoryCollectionWindowOpenedEmailService.Sent
            .Where(s => s.CollectionName == name)
            .Should().OnlyContain(s => s.PeriodLabel == "Maturity assessment 2026/27");

        // BLUM-8094: the DHSC summary must select the maturity template, not the quarterly one — Notify's
        // ((condition??text)) syntax can't express this branch inside one shared template (see
        // NotifyCollectionWindowOpenedSummaryEmailService), so this flag is what tells it which template id to use.
        InMemoryCollectionWindowOpenedSummaryEmailService.Sent
            .Where(s => s.CollectionName == name)
            .Should().OnlyContain(s => s.IsMaturityAssessment);
    }

    [Fact]
    public async Task ShouldFire_ForACollectionWithNoConfiguredReminders()
    {
        const string name = "Window opened no reminders";
        var id = await CreateAsync(BaseRequest(name));

        // The "automatic, not admin-configured" decision. Every fixture in this class already omits reminders,
        // but this states it: the announcement is owed whether or not a cadence was set up, which is why the
        // sweep's candidate query stopped filtering on Reminders.Any().
        (await Context.Collections.AsNoTracking()
            .Include(c => c.Reminders)
            .SingleAsync(c => c.Id == new CollectionId(id)))
            .Reminders.Should().BeEmpty();

        (await SweepAsync()).Should().Be(1);

        WindowOpenedRecipientsFor(name).Should().NotBeEmpty();
    }

    [Fact]
    public async Task ShouldAnnounceOnlyOnce_EvenAfterTheAdminMovesTheOpenDate()
    {
        const string name = "Window opened once";
        var id = await CreateAsync(BaseRequest(name));

        (await SweepAsync()).Should().Be(1);
        var first = WindowOpenedRecipientsFor(name);
        first.Should().NotBeEmpty();

        (await SweepAsync()).Should().Be(0);

        // The claim is once per COLLECTION, not once per date — and this is the reason. CollectionOpenDate is
        // mutable while a window is open, and UpdateCollectionCommand even recomputes it from the financial year
        // and quarter when WindowSameAsReportingPeriod is true. Under a date-keyed claim an admin correcting an
        // unrelated field would re-announce the opening to every Local Authority in the country.
        await MoveOpenDateAsync(id, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1));

        (await SweepAsync()).Should().Be(0);
        WindowOpenedRecipientsFor(name).Should().BeEquivalentTo(first);

        (await DispatchesForAsync(id)).Should().ContainSingle()
            .Which.TriggerType.Should().Be(ReminderTrigger.WindowOpened);
    }

    [Fact]
    public async Task ShouldNotFire_ForADraftScheduledClosedOrDeletedWindow()
    {
        var draft = await CreateAsync(DraftRequest("Window opened draft"));
        var scheduled = await CreateAsync(ScheduledRequest("Window opened scheduled"));

        var closed = await CreateAsync(BaseRequest("Window opened closed"));
        (await Client.PostAsync($"{Route}/{closed}/close", null)).IsSuccessStatusCode.Should().BeTrue();

        var deleted = await CreateAsync(BaseRequest("Window opened deleted"));
        (await Client.DeleteAsync($"{Route}/{deleted}")).IsSuccessStatusCode.Should().BeTrue();

        (await SweepAsync()).Should().Be(0);

        WindowOpenedRecipientsFor("Window opened draft").Should().BeEmpty();
        WindowOpenedRecipientsFor("Window opened scheduled").Should().BeEmpty();
        WindowOpenedRecipientsFor("Window opened closed").Should().BeEmpty();
        WindowOpenedRecipientsFor("Window opened deleted").Should().BeEmpty();

        (await DispatchesForAsync(draft)).Should().BeEmpty();
        (await DispatchesForAsync(scheduled)).Should().BeEmpty();
    }

    [Fact]
    public async Task ShouldStillFire_WhenTheWindowOpenedWellBeforeThePublishDate()
    {
        const string name = "Window opened weeks ago";
        var maxAgeDays = _settings.CollectionNotifications.MaxReminderNotificationAgeDays;

        var id = await CreateAsync(BaseRequest(name));

        // The commonest setup there is, and the one a catch-up floor would silently kill: DHSC publishes the Q1
        // collection a fortnight into Q1 with "window same as reporting period" ticked, so the open date is
        // already 1 April. No validator forbids a past open date, and the deadline is still months away, so the
        // collection is genuinely Open — the announcement is owed and nobody has had it.
        //
        // Repeats are bounded by the once-per-collection claim rather than by a date floor, which is what
        // ShouldAnnounceOnlyOnce_EvenAfterTheAdminMovesTheOpenDate covers.
        await MoveOpenDateAsync(id, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-(maxAgeDays + 21)));

        (await SweepAsync()).Should().Be(1);
        WindowOpenedRecipientsFor(name).Should().NotBeEmpty();
        (await DispatchesForAsync(id)).Should().ContainSingle();
    }

    [Fact]
    public async Task ShouldReleaseTheClaim_WhenOnlyTheProviderTemplateIsMissing()
    {
        const string name = "Window opened provider template missing";
        var id = await CreateAsync(BaseRequest(name));

        // The two template ids are independent App Service settings and both ship empty, so DHSC filling one in
        // before the other is the expected order of events, not a corner case. If the summary were allowed to
        // send while the provider half was refused, something WOULD have been delivered, the general release
        // below would not fire, and the row would be stamped — and because the claim is once per collection with
        // no date component, that collection's organisations could never be told their window had opened. AC1 is
        // the primary criterion; losing it permanently is not a partial success.
        InMemoryCollectionWindowOpenedEmailService.NotConfiguredFor(name);

        (await SweepAsync()).Should().Be(0);
        (await DispatchesForAsync(id)).Should().BeEmpty();

        // And the summary is not sent meanwhile, so the retry cannot spam DHSC admins four times a day for as
        // long as the other template is missing.
        SummaryRecipientsFor(name).Should().BeEmpty();

        InMemoryCollectionWindowOpenedEmailService.ConfigureFor(name);

        (await SweepAsync()).Should().Be(1);
        WindowOpenedRecipientsFor(name).Should().NotBeEmpty();
        SummaryRecipientsFor(name).Should().NotBeEmpty();
    }

    [Fact]
    public async Task ShouldRetryOnTheNextSweep_WhenNotifyIsNotConfiguredYet()
    {
        const string name = "Window opened unconfigured";
        var id = await CreateAsync(BaseRequest(name));

        // The state this feature ships in: both template ids are empty until DHSC creates the templates, so every
        // send is refused before it is attempted. That is not a send that failed — nothing was delivered and
        // nothing could have been — so the claim must be released, or every window that opened before the
        // templates existed would be marked announced forever.
        InMemoryCollectionWindowOpenedEmailService.NotConfiguredFor(name);
        InMemoryCollectionWindowOpenedSummaryEmailService.NotConfiguredFor(name);

        (await SweepAsync()).Should().Be(0);
        WindowOpenedRecipientsFor(name).Should().BeEmpty();
        (await DispatchesForAsync(id)).Should().BeEmpty();

        InMemoryCollectionWindowOpenedEmailService.ConfigureFor(name);
        InMemoryCollectionWindowOpenedSummaryEmailService.ConfigureFor(name);

        (await SweepAsync()).Should().Be(1);
        WindowOpenedRecipientsFor(name).Should().NotBeEmpty();
        (await DispatchesForAsync(id)).Should().ContainSingle();
    }

    [Fact]
    public async Task ShouldStillRecordTheDispatch_WhenSendsFail()
    {
        const string name = "Window opened failing";
        var id = await CreateAsync(BaseRequest(name));

        // The other half of the release branch. NotifyEmailServiceBase bounds how long it WAITS but cannot abort
        // an HTTP call the SDK has started, so a degraded-but-working Notify reports failure for mail that really
        // arrived. Retrying that would announce the window twice to everyone who did receive it.
        InMemoryCollectionWindowOpenedEmailService.FailFor(name);

        (await SweepAsync()).Should().Be(1);
        WindowOpenedRecipientsFor(name).Should().BeEmpty();
        (await DispatchesForAsync(id)).Should().ContainSingle().Which.SentAt.Should().NotBeNull();

        InMemoryCollectionWindowOpenedEmailService.StopFailingFor(name);

        (await SweepAsync()).Should().Be(0);
        WindowOpenedRecipientsFor(name).Should().BeEmpty();
    }

    [Fact]
    public async Task ShouldReleaseTheClaim_WhenTheSummaryIsUnconfiguredAndNoOrganisationsAreAssigned()
    {
        const string name = "Window opened nobody assigned";

        // The provider half has nothing to do, so it cannot report "not configured" — but the DHSC summary is
        // still owed (AC2 is unconditional) and is still refused for missing config. Keeping the claim here would
        // consume the announcement in exactly the state this feature ships in, and the summary would never go out
        // once DHSC creates the template. This is the HadOtherFailure distinction, on the new path.
        //
        // The window names one organisation, and it is the SUSPENDED one — the validator refuses an empty
        // assignment list, and the suspension filter then leaves the provider half with nobody to write to.
        var id = await CreateAsync(BaseRequest(name) with
        {
            IsSubmittedByAllLas = false,
            LocalAuthorityIds = new[] { SuspendedOrganisation.Id.Value },
        });

        InMemoryCollectionWindowOpenedEmailService.NotConfiguredFor(name);
        InMemoryCollectionWindowOpenedSummaryEmailService.NotConfiguredFor(name);

        (await SweepAsync()).Should().Be(0);
        (await DispatchesForAsync(id)).Should().BeEmpty();

        InMemoryCollectionWindowOpenedSummaryEmailService.ConfigureFor(name);

        (await SweepAsync()).Should().Be(1);
        SummaryRecipientsFor(name).Should().BeEquivalentTo(await ActiveAdminEmailsAsync());
        InMemoryCollectionWindowOpenedSummaryEmailService.Sent
            .Where(s => s.CollectionName == name)
            .Should().OnlyContain(s => s.AssignedCount == 0);
    }

    [Fact]
    public async Task ShouldNotEmailSuspendedOrganisationsDeactivatedUsersOrVcses()
    {
        const string name = "Window opened recipient scoping";
        await CreateAsync(BaseRequest(name));

        await SweepAsync();

        var recipients = WindowOpenedRecipientsFor(name);

        // A suspended organisation's users are blocked from the API by OrganisationSuspendedMiddleware, so
        // "the window is open, sign in and report" is unactionable. All three exclusions are seeded, so none of
        // these assertions passes vacuously.
        recipients.Should().NotContain(SuspendedOrgUserEmail);
        recipients.Should().NotContain(DeactivatedOrgUser.Email);
        recipients.Should().NotContain(VcseUserEmail);
    }

    // -------------------------------------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------------------------------------

    private static string SuspendedOrgUserEmail => "window.opened.suspended@org.com";

    private static string VcseUserEmail => "window.opened.vcse@org.com";

    private async Task<int> SweepAsync()
    {
        // CreateAsyncScope, not CreateScope: RepositoryBase implements IAsyncDisposable only, and the sync
        // Dispose of a scope holding one throws.
        await using var scope = _factory.Services.CreateAsyncScope();
        var notifier = scope.ServiceProvider.GetRequiredService<ICollectionReminderNotifier>();

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

    private static IReadOnlyList<string> WindowOpenedRecipientsFor(string collectionName) =>
        InMemoryCollectionWindowOpenedEmailService.Sent
            .Where(s => s.CollectionName == collectionName)
            .Select(s => s.Email)
            .ToList();

    private static IReadOnlyList<string> SummaryRecipientsFor(string collectionName) =>
        InMemoryCollectionWindowOpenedSummaryEmailService.Sent
            .Where(s => s.CollectionName == collectionName)
            .Select(s => s.Email)
            .ToList();

    private async Task<IReadOnlyList<CollectionReminderDispatch>> DispatchesForAsync(Guid id)
    {
        Context.ChangeTracker.Clear();

        return await Context.CollectionReminderDispatches
            .AsNoTracking()
            .Where(d => d.CollectionId == new CollectionId(id))
            .OrderBy(d => d.CreatedAt)
            .ToListAsync();
    }

    // CollectionOpenDate has a private setter, and UpdateDetails refuses once the collection is Closed, so the
    // change tracker is the honest way to move it — the same approach CollectionReminderNotificationTests takes
    // to the deadline.
    private async Task MoveOpenDateAsync(Guid id, DateOnly openDate)
    {
        Context.ChangeTracker.Clear();

        var collection = await Context.Collections.SingleAsync(c => c.Id == new CollectionId(id));
        Context.Entry(collection).Property(c => c.CollectionOpenDate).CurrentValue = openDate;
        await SaveChangesAsync();
    }

    private async Task SeedSubmittedSubmissionAsync(Guid collectionId, Organisation organisation)
    {
        var submitter = User.New(
            UserId.New(),
            new Name("WindowOpened", "Submitter"),
            $"window.opened.submitter.{Guid.NewGuid():N}@example.gov.uk",
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

    private CreateCollectionRequestDto ScheduledRequest(string name)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        return BaseRequest(name) with { OpenDate = today.AddDays(5), Deadline = today.AddDays(25) };
    }

    // Opens two days ago — inside the catch-up floor, and the opposite of
    // CollectionReminderNotificationTests.BaseRequest, which deliberately opens outside it. No reminders are
    // configured on any fixture here: the announcement does not need them, and their absence is what
    // ShouldFire_ForACollectionWithNoConfiguredReminders is about.
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
            OpenDate: today.AddDays(-2),
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
        // the others exist so the recipient filtering is actually exercised rather than passing vacuously.
        foreach (var user in new[] { AdminsData.MainAdmin, AdminsData.SecondAdmin, AdminsData.DeactivatedAdmin, LaLead })
        {
            if (!await Context.Users.AsNoTracking().AnyAsync(u => u.Id == user.Id))
            {
                await Context.Users.AddAsync(user);
            }
        }

        foreach (var organisation in new[] { MainOrg, SecondOrg, SuspendedOrganisation, VcseOrganisation })
        {
            if (!await Context.Organisations.AsNoTracking().AnyAsync(o => o.Id == organisation.Id))
            {
                await Context.Organisations.AddAsync(organisation);
            }
        }

        await SaveChangesAsync();

        _mainOrgUser = await EnsureOrganisationUserAsync(MainOrg, "window.opened.main@org.com", UserStatus.Active);
        _secondOrgUser = await EnsureOrganisationUserAsync(SecondOrg, "window.opened.second@org.com", UserStatus.Active);
        await EnsureOrganisationUserAsync(SuspendedOrganisation, SuspendedOrgUserEmail, UserStatus.Active);
        await EnsureOrganisationUserAsync(VcseOrganisation, VcseUserEmail, UserStatus.Active);

        // Deactivated, and attached to an assigned organisation, so the status filter in the bulk contact query
        // is genuinely exercised.
        await EnsureOrganisationUserAsync(MainOrg, DeactivatedOrgUser.Email, UserStatus.Deactivated);

        Context.CollectionReminderDispatches.RemoveRange(Context.CollectionReminderDispatches);
        Context.Submissions.RemoveRange(Context.Submissions);
        Context.Collections.RemoveRange(Context.Collections);
        Context.AuditLogs.RemoveRange(Context.AuditLogs);
        await SaveChangesAsync();

        _formModuleId = (await Context.FormModules.AsNoTracking().FirstAsync()).Id.Value;

        // The recorders are static and CollectionReminderNotificationTests writes into them too (its last two
        // facts cover where the two passes meet), so Clear() is NOT called here: xUnit runs two classes at a
        // time and it would wipe that class's records mid-test. Every assertion below filters on a collection
        // name unique to its test, which is the isolation that actually holds. Only the per-collection switches
        // are reset, and those are keyed by name so they cannot reach another class either.
        InMemoryCollectionWindowOpenedEmailService.ResetSwitches();
        InMemoryCollectionWindowOpenedSummaryEmailService.ResetSwitches();
        AuthenticateAsAdmin();
    }

    private async Task<OrganisationUser> EnsureOrganisationUserAsync(Organisation organisation, string email, UserStatus status)
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
            name: new Name("WindowOpened", "Contact"),
            email: email,
            subId: new SubId(Guid.NewGuid()),
            status: status,
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
        // The recorders are static and CollectionReminderNotificationTests writes into them too (its last two
        // facts cover where the two passes meet), so Clear() is NOT called here: xUnit runs two classes at a
        // time and it would wipe that class's records mid-test. Every assertion below filters on a collection
        // name unique to its test, which is the isolation that actually holds. Only the per-collection switches
        // are reset, and those are keyed by name so they cannot reach another class either.
        InMemoryCollectionWindowOpenedEmailService.ResetSwitches();
        InMemoryCollectionWindowOpenedSummaryEmailService.ResetSwitches();

        Context.CollectionReminderDispatches.RemoveRange(Context.CollectionReminderDispatches);
        Context.Submissions.RemoveRange(Context.Submissions);
        Context.Collections.RemoveRange(Context.Collections);
        Context.AuditLogs.RemoveRange(Context.AuditLogs);
        await SaveChangesAsync();

        AuthenticateAsAdmin();
    }
}