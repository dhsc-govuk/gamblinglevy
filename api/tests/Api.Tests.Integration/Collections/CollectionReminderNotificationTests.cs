using System.Net;
using System.Net.Http.Json;
using Api.Dtos;
using Application.Common.Interfaces;
using Application.Common.Settings;
using Application.MaturityAssessments.Protocol;
using Domain.AuditLogs;
using Domain.Collections;
using Domain.DataCollectionForms;
using Domain.MaturityAssessments;
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
/// BLUM-7571 — sending a deadline reminder to the data providers that have not yet submitted, and a summary to
/// DHSC Super Admins.
///
/// Both entry points are covered: the sweep, which resolves each configured reminder's OFFSET to a calendar date
/// and fires when that date has been reached, and the manual button, which fires regardless of the cadence. They
/// share CollectionReminderNotifier's dispatch path, and the tests here assert both go through the same
/// already-submitted and suspended-organisation filters.
///
/// The sweep is driven through ICollectionReminderNotifier directly rather than through the hosted service that
/// schedules it: IntegrationTestWebFactory unhooks that worker for the whole suite (RemoveBackgroundServices),
/// and waiting on a PeriodicTimer would be slow and flaky for no extra coverage.
///
/// Dates are pushed around through the change tracker rather than by creating a collection with them, because the
/// create validator refuses a past deadline — which is exactly right and exactly unhelpful here.
/// </summary>
public class CollectionReminderNotificationTests : BaseIntegrationTest, IAsyncLifetime
{
    private const string Route = "data-collections";

    private static readonly Organisation SuspendedOrganisation = Organisation.New(
        id: OrganisationId.New(),
        name: "Suspended Council",
        onsCode: "E06000903",
        region: Region.SouthWest,
        type: OrgType.LocalAuthority,
        eCode: "E1000903",
        onboardingStatus: OnboardingStatus.Suspended,
        utcNow: DateTime.UtcNow);

    private static readonly Organisation VcseOrganisation = Organisation.New(
        id: OrganisationId.New(),
        name: "Reminder VCSE",
        onsCode: null,
        region: Region.London,
        type: OrgType.Vcse,
        eCode: "E1000904",
        onboardingStatus: OnboardingStatus.Active,
        utcNow: DateTime.UtcNow);

    private static readonly User LaLead = User.New(
        id: UserId.New(),
        name: new Name("Reminder", "Lead"),
        email: "reminder.la.lead@email.com",
        subId: new SubId(Guid.NewGuid()),
        status: UserStatus.Active,
        role: UserRole.Lead,
        utcNow: DateTime.UtcNow);

    private static readonly User DeactivatedOrgUser = User.New(
        id: UserId.New(),
        name: new Name("Reminder", "Deactivated"),
        email: "reminder.deactivated@org.com",
        subId: new SubId(Guid.NewGuid()),
        status: UserStatus.Active,
        role: UserRole.OrganisationAdmin,
        utcNow: DateTime.UtcNow);

    private readonly IntegrationTestWebFactory _factory;
    private readonly ApplicationSettings _settings;

    private Guid _formModuleId;
    private OrganisationUser _mainOrgUser = null!;
    private OrganisationUser _secondOrgUser = null!;

    public CollectionReminderNotificationTests(IntegrationTestWebFactory factory)
        : base(factory)
    {
        _factory = factory;
        _settings = factory.Services.GetRequiredService<ApplicationSettings>();
    }

    private static Organisation MainOrg => OrganisationsData.MainOrganisation;

    private static Organisation SecondOrg => OrganisationsData.SecondOrganisation;

    private sealed record CollectionResult(Guid Id, string Name, string Status, bool IsDraft);

    // ---------------------------------------------------------------------------------------------------------
    // The sweep
    // ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ShouldEmailOnlyOrganisationsThatHaveNotSubmitted()
    {
        const string name = "Outstanding only window";
        var id = await CreateWithReminderAsync(name, ReminderOffsetTypes.BeforeDeadline, days: 7);

        // SecondOrg is finished; MainOrg is not. AC3.
        await SeedSubmittedSubmissionAsync(id, SecondOrg);

        await MakeReminderDueAsync(id, daysBeforeDeadline: 7);

        (await SweepAsync()).Should().Be(1);

        ProviderRecipientsFor(name).Should().BeEquivalentTo([_mainOrgUser.User!.Email]);
        ProviderRecipientsFor(name).Should().NotContain(_secondOrgUser.User!.Email);
    }

    [Fact]
    public async Task ShouldEmailOnlyOrganisationsThatHaveNotSubmitted_ForAMaturityAssessmentWindow()
    {
        // A maturity window's "already submitted" set lives in maturity_assessments, not submissions (BLUM-7855);
        // before the notifier branched on the collection type, every maturity window reminded everyone forever.
        const string name = "Maturity outstanding only window";
        var id = await CreateWithReminderAsync(name, ReminderOffsetTypes.BeforeDeadline, days: 7, maturity: true);

        await SeedSubmittedMaturityAssessmentAsync(id, SecondOrg);
        await MakeReminderDueAsync(id, daysBeforeDeadline: 7);

        (await SweepAsync()).Should().Be(1);

        ProviderRecipientsFor(name).Should().BeEquivalentTo([_mainOrgUser.User!.Email]);
        InMemoryCollectionReminderSummaryEmailService.Sent
            .Where(s => s.CollectionName == name)
            .Should().OnlyContain(s => s.OutstandingCount == 1 && s.IsMaturityAssessment);
    }

    [Fact]
    public async Task ShouldStateTheCollectionNameAndDeadline()
    {
        const string name = "Stated deadline window";
        var id = await CreateWithReminderAsync(name, ReminderOffsetTypes.BeforeDeadline, days: 7);
        var deadline = await MakeReminderDueAsync(id, daysBeforeDeadline: 7);

        await SweepAsync();

        // AC2 is about what the email SAYS, so assert the recorded payload rather than only that a send happened.
        var sent = InMemoryCollectionReminderEmailService.Sent.Where(s => s.CollectionName == name).ToList();
        sent.Should().NotBeEmpty();
        sent.Should().OnlyContain(s => s.SubmissionDeadline == deadline);
        sent.Should().Contain(s => s.RecipientName == _mainOrgUser.User!.Name.ToString());
    }

    [Fact]
    public async Task ShouldEmailEveryActiveDhscAdmin_AndConfirmTheOutstandingCount()
    {
        const string name = "DHSC summary window";
        var id = await CreateWithReminderAsync(name, ReminderOffsetTypes.BeforeDeadline, days: 7);
        var deadline = await MakeReminderDueAsync(id, daysBeforeDeadline: 7);

        await SweepAsync();

        // Compared against the database rather than a hardcoded list — the app seeds a default admin at boot, so
        // "every active admin" is more than the users this class adds. AC5.
        SummaryRecipientsFor(name).Should().BeEquivalentTo(await ActiveAdminEmailsAsync());

        // AC6: the deadline, plus the count that makes "a reminder has been sent to outstanding local
        // authorities" honest. MainOrg and SecondOrg are outstanding; the suspended one is excluded.
        var summaries = InMemoryCollectionReminderSummaryEmailService.Sent.Where(s => s.CollectionName == name).ToList();
        summaries.Should().OnlyContain(s => s.SubmissionDeadline == deadline);
        summaries.Should().OnlyContain(s => s.OutstandingCount == 2);

        // The template's is_quarterly_report / is_maturity_assessment pair must be mutually exclusive per send —
        // a quarterly window (no `maturity: true` above) should never be flagged as a maturity assessment.
        summaries.Should().OnlyContain(s => !s.IsMaturityAssessment);
    }

    [Fact]
    public async Task ShouldStillSummariseToDhsc_WhenNobodyIsOutstanding()
    {
        const string name = "Nobody outstanding window";
        var id = await CreateWithReminderAsync(name, ReminderOffsetTypes.BeforeDeadline, days: 7);
        await SeedSubmittedSubmissionAsync(id, MainOrg);
        await SeedSubmittedSubmissionAsync(id, SecondOrg);
        await MakeReminderDueAsync(id, daysBeforeDeadline: 7);

        (await SweepAsync()).Should().Be(1);

        // AC5 is unconditional. The zero is what the Notify template has to branch on, so it must actually reach
        // the summary rather than the whole dispatch being skipped.
        ProviderRecipientsFor(name).Should().BeEmpty();
        SummaryRecipientsFor(name).Should().BeEquivalentTo(await ActiveAdminEmailsAsync());
        InMemoryCollectionReminderSummaryEmailService.Sent
            .Where(s => s.CollectionName == name)
            .Should().OnlyContain(s => s.OutstandingCount == 0);
    }

    [Fact]
    public async Task ShouldFireOnlyOnce_ForOneTriggerPoint()
    {
        const string name = "Fire once window";
        var id = await CreateWithReminderAsync(name, ReminderOffsetTypes.BeforeDeadline, days: 7);
        await MakeReminderDueAsync(id, daysBeforeDeadline: 7);

        (await SweepAsync()).Should().Be(1);
        var firstSweep = ProviderRecipientsFor(name);
        firstSweep.Should().NotBeEmpty();

        // The whole point of the dispatch row: the sweep runs on a timer AND at every cold start, so without it
        // every run would re-send. A single-sweep assertion passes against a version with no dispatch table.
        (await SweepAsync()).Should().Be(0);
        ProviderRecipientsFor(name).Should().BeEquivalentTo(firstSweep);

        (await DispatchesForAsync(id)).Should().ContainSingle()
            .Which.SentAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ShouldStillNotResend_AfterTheAdminEditsTheCollection()
    {
        const string name = "Edited after sending window";
        var id = await CreateWithReminderAsync(name, ReminderOffsetTypes.BeforeDeadline, days: 7);
        await MakeReminderDueAsync(id, daysBeforeDeadline: 7);

        (await SweepAsync()).Should().Be(1);
        var firstSweep = ProviderRecipientsFor(name);

        // This is the test the whole idempotency design exists for. Collection.SetReminders CLEARS the reminder
        // list and re-creates every row with a fresh CollectionReminderId, so ANY save of the collection — even
        // one that changed nothing about the reminders — would wipe a "sent" stamp held on the reminder row.
        // Keying the dispatch on the resolved calendar date instead is what survives this.
        await UpdateRemindersUnchangedAsync(id, name);

        (await SweepAsync()).Should().Be(0);
        ProviderRecipientsFor(name).Should().BeEquivalentTo(firstSweep);
    }

    [Fact]
    public async Task ShouldFireAgain_WhenTheDeadlineMoves()
    {
        const string name = "Moved deadline window";
        var id = await CreateWithReminderAsync(name, ReminderOffsetTypes.BeforeDeadline, days: 7);
        await MakeReminderDueAsync(id, daysBeforeDeadline: 7);

        (await SweepAsync()).Should().Be(1);

        // Moving the deadline turns "7 days before" into a different calendar date — a genuinely new trigger
        // point that outstanding organisations should be told about.
        await MakeReminderDueAsync(id, daysBeforeDeadline: 7, extraDaysAgo: 2);

        (await SweepAsync()).Should().Be(1);

        var dispatches = await DispatchesForAsync(id);
        dispatches.Should().HaveCount(2);
        dispatches.Select(d => d.ScheduledFor).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task ShouldNotFire_ForADraftScheduledOrClosedWindow()
    {
        var draft = await CreateAsync(DraftRequest("Reminder draft window") with { Reminders = Reminder(ReminderOffsetTypes.BeforeDeadline, 7) });
        var scheduled = await CreateAsync(ScheduledRequest("Reminder scheduled window") with { Reminders = Reminder(ReminderOffsetTypes.AfterOpen, 0) });

        var closed = await CreateWithReminderAsync("Reminder closed window", ReminderOffsetTypes.BeforeDeadline, days: 7);
        (await Client.PostAsync($"{Route}/{closed}/close", null)).IsSuccessStatusCode.Should().BeTrue();

        var deleted = await CreateWithReminderAsync("Reminder deleted window", ReminderOffsetTypes.BeforeDeadline, days: 7);
        await MakeReminderDueAsync(deleted, daysBeforeDeadline: 7);
        (await Client.DeleteAsync($"{Route}/{deleted}")).IsSuccessStatusCode.Should().BeTrue();

        (await SweepAsync()).Should().Be(0);

        ProviderRecipientsFor("Reminder draft window").Should().BeEmpty();
        ProviderRecipientsFor("Reminder scheduled window").Should().BeEmpty();
        ProviderRecipientsFor("Reminder closed window").Should().BeEmpty();
        ProviderRecipientsFor("Reminder deleted window").Should().BeEmpty();

        (await DispatchesForAsync(draft)).Should().BeEmpty();
        (await DispatchesForAsync(scheduled)).Should().BeEmpty();
    }

    [Fact]
    public async Task ShouldNotFire_WhenTheTriggerPointIsOlderThanTheMaxAge()
    {
        const string name = "Long past trigger window";
        var maxAgeDays = _settings.CollectionNotifications.MaxReminderNotificationAgeDays;

        var id = await CreateWithReminderAsync(name, ReminderOffsetTypes.BeforeDeadline, days: 7);
        await MakeReminderDueAsync(id, daysBeforeDeadline: 7, extraDaysAgo: maxAgeDays + 1);

        // The backstop behind the deploy-time backfill: anything that empties collection_reminder_dispatches — a
        // restore from backup, a data reset — must not mail every outstanding organisation once per historical
        // fire date.
        (await SweepAsync()).Should().Be(0);
        ProviderRecipientsFor(name).Should().BeEmpty();
    }

    [Fact]
    public async Task ShouldRetryOnTheNextSweep_WhenNotifyIsNotConfiguredYet()
    {
        const string name = "Unconfigured reminder window";
        var id = await CreateWithReminderAsync(name, ReminderOffsetTypes.BeforeDeadline, days: 7);
        await MakeReminderDueAsync(id, daysBeforeDeadline: 7);

        // The state this feature ships in: both Notify template ids are empty until DHSC creates the templates,
        // so every send is refused before it is attempted. That is not a send that failed — nothing was delivered
        // and nothing could have been — so the claim must be released, or every trigger point reached before the
        // templates exist would be marked done forever.
        InMemoryCollectionReminderEmailService.NotConfiguredFor(name);
        InMemoryCollectionReminderSummaryEmailService.NotConfiguredFor(name);

        (await SweepAsync()).Should().Be(0);
        ProviderRecipientsFor(name).Should().BeEmpty();
        (await DispatchesForAsync(id)).Should().BeEmpty();

        // So the backlog goes out once the Notify__* settings are filled in.
        InMemoryCollectionReminderEmailService.ConfigureFor(name);
        InMemoryCollectionReminderSummaryEmailService.ConfigureFor(name);

        (await SweepAsync()).Should().Be(1);
        ProviderRecipientsFor(name).Should().NotBeEmpty();
        (await DispatchesForAsync(id)).Should().ContainSingle();
    }

    [Fact]
    public async Task ShouldStillRecordTheDispatch_WhenSendsFail()
    {
        const string name = "Failing reminder window";
        var id = await CreateWithReminderAsync(name, ReminderOffsetTypes.BeforeDeadline, days: 7);
        await MakeReminderDueAsync(id, daysBeforeDeadline: 7);

        InMemoryCollectionReminderEmailService.FailFor(name);

        (await SweepAsync()).Should().Be(1);
        ProviderRecipientsFor(name).Should().BeEmpty();

        // Kept even though nothing reached a provider, and that is the point rather than an accident:
        // NotifyEmailServiceBase bounds how long it WAITS but cannot abort the HTTP call the Notify SDK has
        // already started, so a degraded-but-working Notify returns a failure for mail that really did arrive.
        // Retrying it would re-mail every organisation that did receive one.
        (await DispatchesForAsync(id)).Should().ContainSingle().Which.SentAt.Should().NotBeNull();

        InMemoryCollectionReminderEmailService.StopFailingFor(name);

        (await SweepAsync()).Should().Be(0);
        ProviderRecipientsFor(name).Should().BeEmpty();
    }

    [Fact]
    public async Task ShouldEmailEveryAssignedLa_WhenTheWindowIsSubmittedByAllLas()
    {
        const string name = "All LAs reminder window";
        var id = await CreateWithReminderAsync(name, ReminderOffsetTypes.BeforeDeadline, days: 7);
        await MakeReminderDueAsync(id, daysBeforeDeadline: 7);

        (await SweepAsync()).Should().Be(1);

        // An all-LAs window carries NO collection_local_authorities rows, which is exactly what the previous
        // manual-reminder implementation read — so it reached nobody at all. Resolution goes through
        // SubmissionAssignment instead, which also excludes VCSEs from the implicit case.
        ProviderRecipientsFor(name).Should().BeEquivalentTo([_mainOrgUser.User!.Email, _secondOrgUser.User!.Email]);
    }

    [Fact]
    public async Task ShouldEmailTheAssignedLas_WhenTheWindowNamesThemExplicitly()
    {
        const string name = "Explicitly assigned window";

        // Every other test here uses IsSubmittedByAllLas: true, which resolves organisations without reading the
        // assignment rows at all. The explicit branch of SubmissionAssignment reads collection.LocalAuthorities,
        // so the sweep's query has to load them — and a missing Include fails silently: no recipients, an
        // outstanding count of 0, and the trigger point consumed anyway.
        var id = await CreateAsync(BaseRequest(name) with
        {
            IsSubmittedByAllLas = false,
            LocalAuthorityIds = new[] { MainOrg.Id.Value },
            Reminders = Reminder(ReminderOffsetTypes.BeforeDeadline, 7),
        });

        await MakeReminderDueAsync(id, daysBeforeDeadline: 7);

        (await SweepAsync()).Should().Be(1);

        ProviderRecipientsFor(name).Should().BeEquivalentTo([_mainOrgUser.User!.Email]);
        ProviderRecipientsFor(name).Should().NotContain(_secondOrgUser.User!.Email);

        InMemoryCollectionReminderSummaryEmailService.Sent
            .Where(s => s.CollectionName == name)
            .Should().OnlyContain(s => s.OutstandingCount == 1);
    }

    [Fact]
    public async Task ShouldReleaseTheClaim_WhenTheSummaryIsUnconfiguredAndNobodyIsOutstanding()
    {
        const string name = "Unconfigured summary nobody outstanding";
        var id = await CreateWithReminderAsync(name, ReminderOffsetTypes.BeforeDeadline, days: 7);
        await SeedSubmittedSubmissionAsync(id, MainOrg);
        await SeedSubmittedSubmissionAsync(id, SecondOrg);
        await MakeReminderDueAsync(id, daysBeforeDeadline: 7);

        // The provider half has nothing to do, so it cannot report "not configured" — but the DHSC summary is
        // still owed (AC5 is unconditional) and is still refused for missing config. Keeping the claim here
        // would consume the trigger point in exactly the state this feature ships in, and the summary would
        // never go out once DHSC creates the template.
        InMemoryCollectionReminderEmailService.NotConfiguredFor(name);
        InMemoryCollectionReminderSummaryEmailService.NotConfiguredFor(name);

        (await SweepAsync()).Should().Be(0);
        (await DispatchesForAsync(id)).Should().BeEmpty();

        InMemoryCollectionReminderSummaryEmailService.ConfigureFor(name);

        (await SweepAsync()).Should().Be(1);
        SummaryRecipientsFor(name).Should().BeEquivalentTo(await ActiveAdminEmailsAsync());
    }

    [Fact]
    public async Task ShouldKeepTheClaim_WhenProviderSendsFailedAndOnlyTheSummaryIsUnconfigured()
    {
        const string name = "Failed provider unconfigured summary";
        var id = await CreateWithReminderAsync(name, ReminderOffsetTypes.BeforeDeadline, days: 7);
        await MakeReminderDueAsync(id, daysBeforeDeadline: 7);

        // A send that was ATTEMPTED and failed is not the same as one refused for missing config:
        // NotifyEmailServiceBase cannot abort an HTTP call the SDK has started, so a "failure" may be mail that
        // really arrived. Releasing the claim here would re-mail every one of those organisations.
        InMemoryCollectionReminderEmailService.FailFor(name);
        InMemoryCollectionReminderSummaryEmailService.NotConfiguredFor(name);

        (await SweepAsync()).Should().Be(1);
        (await DispatchesForAsync(id)).Should().ContainSingle().Which.SentAt.Should().NotBeNull();

        InMemoryCollectionReminderEmailService.StopFailingFor(name);
        InMemoryCollectionReminderSummaryEmailService.ConfigureFor(name);

        (await SweepAsync()).Should().Be(0);
        ProviderRecipientsFor(name).Should().BeEmpty();
    }

    [Fact]
    public async Task ShouldNotEmailDeactivatedUsersOrSuspendedOrganisations()
    {
        const string name = "Recipient scoping reminder window";
        var id = await CreateWithReminderAsync(name, ReminderOffsetTypes.BeforeDeadline, days: 7);
        await MakeReminderDueAsync(id, daysBeforeDeadline: 7);

        await SweepAsync();

        var recipients = ProviderRecipientsFor(name);

        // A suspended organisation's users are blocked from the API by OrganisationSuspendedMiddleware, so
        // "please submit" would be unactionable. Both exclusions are seeded, so neither is vacuous.
        recipients.Should().NotContain(SuspendedOrgUserEmail);
        recipients.Should().NotContain(DeactivatedOrgUser.Email);

        // A VCSE is never implicitly assigned to an all-LAs window.
        recipients.Should().NotContain(VcseUserEmail);
    }

    // ---------------------------------------------------------------------------------------------------------
    // The manual button (AC1's "or a manual reminder is sent")
    // ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ShouldRemindOutstandingOrganisationsOnly_WhenSentManually()
    {
        const string name = "Manual reminder window";
        var id = await CreateWithReminderAsync(name, ReminderOffsetTypes.BeforeDeadline, days: 7);
        await SeedSubmittedSubmissionAsync(id, SecondOrg);

        var response = await Client.PostAsync($"{Route}/{id}/send-reminder", null);

        // The response contract is unchanged — the client's confirmation screen reads the CollectionDto back.
        response.IsSuccessStatusCode.Should().BeTrue();
        (await response.ToResponseModel<CollectionResult>()).Id.Should().Be(id);

        ProviderRecipientsFor(name).Should().BeEquivalentTo([_mainOrgUser.User!.Email]);
        SummaryRecipientsFor(name).Should().BeEquivalentTo(await ActiveAdminEmailsAsync());

        var audit = await Context.AuditLogs.AsNoTracking()
            .Where(x => x.TargetType == AuditTargetType.Collection && x.TargetId == id)
            .ToListAsync();
        audit.Should().ContainSingle(x => x.Action == AuditAction.CollectionReminderSent);

        var dispatch = (await DispatchesForAsync(id)).Should().ContainSingle().Subject;
        dispatch.TriggerType.Should().Be(ReminderTrigger.Manual);
        dispatch.OutstandingOrganisationCount.Should().Be(1);
    }

    [Fact]
    public async Task ShouldAllowASecondManualReminderOnTheSameDay()
    {
        const string name = "Twice manually window";
        var id = await CreateWithReminderAsync(name, ReminderOffsetTypes.BeforeDeadline, days: 7);

        (await Client.PostAsync($"{Route}/{id}/send-reminder", null)).IsSuccessStatusCode.Should().BeTrue();
        (await Client.PostAsync($"{Route}/{id}/send-reminder", null)).IsSuccessStatusCode.Should().BeTrue();

        // The unique index deliberately excludes manual sends: the shipped Reminder screen promises "This will
        // immediately send an email…", and an admin may legitimately press it twice in a day — after adding a
        // contact, say. A unique-violation would surface as a silently swallowed no-send.
        (await DispatchesForAsync(id)).Should().HaveCount(2);
        ProviderRecipientsFor(name).Should().HaveCount(4);
    }

    [Fact]
    public async Task ShouldRefuseAManualReminder_WhenTheWindowIsNotOpen()
    {
        const string name = "Manual on closed window";
        var id = await CreateWithReminderAsync(name, ReminderOffsetTypes.BeforeDeadline, days: 7);
        (await Client.PostAsync($"{Route}/{id}/close", null)).IsSuccessStatusCode.Should().BeTrue();

        var response = await Client.PostAsync($"{Route}/{id}/send-reminder", null);

        // Defence in depth — the View screen already hides the action unless the collection is open.
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        ProviderRecipientsFor(name).Should().BeEmpty();
        (await DispatchesForAsync(id)).Should().BeEmpty();
    }

    [Fact]
    public async Task ShouldNotLetAManualReminderConsumeTheScheduledTriggerPoint()
    {
        const string name = "Manual then scheduled window";
        var id = await CreateWithReminderAsync(name, ReminderOffsetTypes.BeforeDeadline, days: 7);
        await MakeReminderDueAsync(id, daysBeforeDeadline: 7);

        (await Client.PostAsync($"{Route}/{id}/send-reminder", null)).IsSuccessStatusCode.Should().BeTrue();

        // A manual row is recorded with trigger "manual", so it cannot claim the scheduled trigger point that is
        // due the same day. Pressing the button early must not silently cancel the configured reminder.
        (await SweepAsync()).Should().Be(1);

        var dispatches = await DispatchesForAsync(id);
        dispatches.Should().HaveCount(2);
        dispatches.Select(d => d.TriggerType).Should().BeEquivalentTo([ReminderTrigger.Manual, ReminderTrigger.Scheduled]);
    }

    // ---------------------------------------------------------------------------------------------------------
    // Where the two passes meet (BLUM-7721)
    // ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ShouldClaimTheWindowOpenAndDeadlineTriggerPointsSeparately()
    {
        const string name = "Window open and reminder window";

        // The open date is inside the catch-up window here — unlike every other fixture in this class — so both
        // passes fire in one sweep, on DIFFERENT dates.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var id = await CreateAsync(
            BaseRequest(name) with
            {
                OpenDate = today.AddDays(-2),
                Reminders = Reminder(ReminderOffsetTypes.BeforeDeadline, 7),
            },
            alreadyAnnounced: false);

        await MakeReminderDueAsync(id, daysBeforeDeadline: 7);

        (await SweepAsync()).Should().Be(1);

        // Both went out, and each has its own claim row. This is the fact that fails if the claimed-set query
        // keys on (collection, date) alone: a window_opened row would convince the sweep the scheduled reminder
        // had already gone out, or the other way round.
        var dispatches = await AllDispatchesForAsync(id);
        dispatches.Should().HaveCount(2);
        dispatches.Select(d => d.TriggerType)
            .Should().BeEquivalentTo([ReminderTrigger.WindowOpened, ReminderTrigger.Scheduled]);

        WindowOpenedRecipientsFor(name).Should().NotBeEmpty();
        ProviderRecipientsFor(name).Should().NotBeEmpty();
    }

    [Fact]
    public async Task ShouldSendOnlyTheWindowOpenEmail_WhenAReminderLandsOnTheOpenDate()
    {
        const string name = "Reminder on the open date window";

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var id = await CreateAsync(
            BaseRequest(name) with
            {
                OpenDate = today,
                Reminders = Reminder(ReminderOffsetTypes.AfterOpen, 0),
            },
            alreadyAnnounced: false);

        (await SweepAsync()).Should().Be(1);

        // "The reporting window is now open" and "you have not submitted yet" on one morning contradict each
        // other, so the announcement wins.
        WindowOpenedRecipientsFor(name).Should().NotBeEmpty();
        ProviderRecipientsFor(name).Should().BeEmpty();

        // The superseded trigger point is CLAIMED, not skipped. Skipping it would leave it due, and the next
        // sweep six hours later would send the reminder after all — both emails, further apart.
        var dispatches = await AllDispatchesForAsync(id);
        dispatches.Should().HaveCount(2);

        var scheduled = dispatches.Single(d => d.TriggerType.Equals(ReminderTrigger.Scheduled));
        scheduled.SentAt.Should().NotBeNull();
        scheduled.ProviderEmailsSent.Should().Be(0);
        scheduled.DhscEmailsSent.Should().Be(0);

        (await SweepAsync()).Should().Be(0);
        ProviderRecipientsFor(name).Should().BeEmpty();
    }

    // ---------------------------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------------------------

    private static string SuspendedOrgUserEmail => "reminder.suspended@org.com";

    private static string VcseUserEmail => "reminder.vcse@org.com";

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

    private static IReadOnlyList<string> ProviderRecipientsFor(string collectionName) =>
        InMemoryCollectionReminderEmailService.Sent
            .Where(s => s.CollectionName == collectionName)
            .Select(s => s.Email)
            .ToList();

    private static IReadOnlyList<string> WindowOpenedRecipientsFor(string collectionName) =>
        InMemoryCollectionWindowOpenedEmailService.Sent
            .Where(s => s.CollectionName == collectionName)
            .Select(s => s.Email)
            .ToList();

    private static IReadOnlyList<string> SummaryRecipientsFor(string collectionName) =>
        InMemoryCollectionReminderSummaryEmailService.Sent
            .Where(s => s.CollectionName == collectionName)
            .Select(s => s.Email)
            .ToList();

    // The reminder rows only. Every fixture here is pre-claimed as already announced (see CreateAsync), and that
    // row is scaffolding rather than something these tests are about — counting it would turn every
    // ContainSingle() into a statement about the announcement instead of the reminder.
    private async Task<IReadOnlyList<CollectionReminderDispatch>> DispatchesForAsync(Guid id) =>
        (await AllDispatchesForAsync(id))
            .Where(d => !d.TriggerType.Equals(ReminderTrigger.WindowOpened))
            .ToList();

    private async Task<IReadOnlyList<CollectionReminderDispatch>> AllDispatchesForAsync(Guid id)
    {
        Context.ChangeTracker.Clear();

        return await Context.CollectionReminderDispatches
            .AsNoTracking()
            .Where(d => d.CollectionId == new CollectionId(id))
            .OrderBy(d => d.CreatedAt)
            .ToListAsync();
    }

    // Moves the deadline so that a "days before deadline" reminder resolves to `extraDaysAgo` days ago, and
    // returns the new deadline. SubmissionDeadline has a private setter and no domain method moves it backwards
    // (UpdateDetails refuses once the collection is Closed), so the change tracker is the honest way in.
    private async Task<DateOnly> MakeReminderDueAsync(Guid id, int daysBeforeDeadline, int extraDaysAgo = 0)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var deadline = today.AddDays(daysBeforeDeadline - extraDaysAgo);

        var collection = await Context.Collections.SingleAsync(c => c.Id == new CollectionId(id));
        Context.Entry(collection).Property(c => c.SubmissionDeadline).CurrentValue = deadline;
        await SaveChangesAsync();

        return deadline;
    }

    // Seeds a finalised (SubmittedAt set) submission directly — enough to make an organisation "has submitted"
    // without driving the whole multi-section submit flow.
    private async Task SeedSubmittedSubmissionAsync(Guid collectionId, Organisation organisation)
    {
        var submitter = User.New(
            UserId.New(),
            new Name("Reminder", "Submitter"),
            $"reminder.submitter.{Guid.NewGuid():N}@example.gov.uk",
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

    // Re-saves the collection with the SAME reminder cadence. That is enough to make Collection.SetReminders
    // regenerate every CollectionReminderId, which is the churn the dispatch table exists to survive.
    private async Task UpdateRemindersUnchangedAsync(Guid id, string name)
    {
        Context.ChangeTracker.Clear();
        var collection = await Context.Collections.AsNoTracking().SingleAsync(c => c.Id == new CollectionId(id));

        var request = new UpdateCollectionRequestDto(
            Name: name,
            Purpose: null,
            Type: "quarterly_report",
            FinancialYear: "2026/27",
            Quarter: 1,
            WindowSameAsReportingPeriod: false,
            CollectionOpenDate: collection.CollectionOpenDate,
            SubmissionDeadline: collection.SubmissionDeadline,
            IsSubmittedByAllLas: true,
            SaveAsDraft: false,
            LocalAuthorityIds: null,
            FormModuleIds: new[] { _formModuleId },
            Reminders: Reminder(ReminderOffsetTypes.BeforeDeadline, 7));

        var response = await Client.PutAsJsonAsync($"{Route}/{id}", request);
        response.IsSuccessStatusCode.Should().BeTrue();
    }

    private static ReminderRequestDto[] Reminder(string offsetType, int days) =>
        [new ReminderRequestDto(offsetType, days)];

    private async Task<Guid> CreateWithReminderAsync(string name, string offsetType, int days, bool maturity = false) =>
        await CreateAsync(maturity
            ? BaseRequest(name) with { Type = "maturity_assessment", Quarter = null, FormModuleIds = MaturityCollectionForms.Ids, Reminders = Reminder(offsetType, days) }
            : BaseRequest(name) with { Reminders = Reminder(offsetType, days) });

    private async Task SeedSubmittedMaturityAssessmentAsync(Guid collectionId, Organisation organisation)
    {
        var assessment = MaturityAssessment.Start(
            MaturityAssessmentId.New(),
            new CollectionId(collectionId),
            organisation.Id,
            ["la_capacity"],
            AdminsData.MainAdmin.Id,
            DateTime.UtcNow);
        assessment.SetSectionStatus("la_capacity", MaturityAssessmentSectionStatus.Completed, AdminsData.MainAdmin.Id, DateTime.UtcNow);
        assessment.Submit($"2099-{Random.Shared.Next(1, 9999):0000}", AdminsData.MainAdmin.Id, DateTime.UtcNow);

        await Context.MaturityAssessments.AddAsync(assessment);
        await SaveChangesAsync();
    }

    // Every collection this class creates is recorded as having ALREADY had its BLUM-7721 window-open
    // announcement, unless a test opts out.
    //
    // This class is about the deadline pass, but both passes share one sweep, and the announcement is owed to
    // every open collection regardless of dates or configured reminders — there is no catch-up floor to place a
    // fixture outside of, deliberately (see ReminderSchedule.WindowOpenedOn). Without this, every sweep here
    // would also fan out an announcement and write a second dispatch row, and the counts these tests turn on
    // would all be about something else. Pre-claiming is the honest fixture: a window that opened days ago has
    // normally been announced already.
    private async Task<Guid> CreateAsync(CreateCollectionRequestDto request, bool alreadyAnnounced = true)
    {
        var response = await Client.PostAsJsonAsync(Route, request);
        response.IsSuccessStatusCode.Should().BeTrue();

        var id = (await response.ToResponseModel<CollectionResult>()).Id;

        // Only for a published window that has actually opened: a draft has no open date, and a scheduled one
        // has not reached it, so neither is owed an announcement and claiming one would show up as a dispatch row
        // in tests that assert there are none.
        if (alreadyAnnounced
            && !request.SaveAsDraft
            && request.OpenDate is { } openDate
            && openDate <= DateOnly.FromDateTime(DateTime.UtcNow))
        {
            await MarkWindowAnnouncedAsync(id, openDate);
        }

        return id;
    }

    private async Task MarkWindowAnnouncedAsync(Guid id, DateOnly openDate)
    {
        Context.ChangeTracker.Clear();

        var dispatch = CollectionReminderDispatch.Create(
            CollectionReminderDispatchId.New(),
            new CollectionId(id),
            ReminderTrigger.WindowOpened,
            openDate,
            DateTime.UtcNow);

        dispatch.MarkSent(0, 0, 0, DateTime.UtcNow);

        await Context.CollectionReminderDispatches.AddAsync(dispatch);
        await SaveChangesAsync();
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

            // Comfortably in the past, so a "days before deadline" reminder always resolves after it. Isolation
            // from the BLUM-7721 announcement is NOT done by this date — there is no catch-up floor to sit
            // outside of — but by CreateAsync pre-claiming the announcement; see the comment there. The
            // interaction between the two passes is covered explicitly by the last two facts in this class, and
            // the announcement on its own by CollectionWindowOpenedNotificationTests.
            OpenDate: today.AddDays(-12),
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

        _mainOrgUser = await EnsureOrganisationUserAsync(MainOrg, "reminder.main@org.com", UserStatus.Active);
        _secondOrgUser = await EnsureOrganisationUserAsync(SecondOrg, "reminder.second@org.com", UserStatus.Active);
        await EnsureOrganisationUserAsync(SuspendedOrganisation, SuspendedOrgUserEmail, UserStatus.Active);
        await EnsureOrganisationUserAsync(VcseOrganisation, VcseUserEmail, UserStatus.Active);

        // Deactivated, and attached to an outstanding organisation, so the status filter in the bulk contact
        // query is genuinely exercised.
        await EnsureOrganisationUserAsync(MainOrg, DeactivatedOrgUser.Email, UserStatus.Deactivated);

        Context.CollectionReminderDispatches.RemoveRange(Context.CollectionReminderDispatches);
        Context.MaturityAssessments.RemoveRange(Context.MaturityAssessments);
        Context.Submissions.RemoveRange(Context.Submissions);
        Context.Collections.RemoveRange(Context.Collections);
        Context.AuditLogs.RemoveRange(Context.AuditLogs);
        await SaveChangesAsync();

        _formModuleId = (await Context.FormModules.AsNoTracking().FirstAsync()).Id.Value;

        InMemoryCollectionReminderEmailService.Clear();
        InMemoryCollectionReminderSummaryEmailService.Clear();

        // The window-opened recorders are deliberately NOT cleared here, unlike the two above. Those bags are
        // static, and this is the second class to write into them (CollectionWindowOpenedNotificationTests is the
        // first) — xUnit runs two classes at a time, so a Clear() from either would wipe the other's records
        // mid-test. Every assertion filters on a collection name unique to its test, which is the isolation that
        // actually holds; clearing was only ever belt-and-braces, and here it is the opposite.
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
            name: new Name("Reminder", "Contact"),
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
        InMemoryCollectionReminderEmailService.Clear();
        InMemoryCollectionReminderSummaryEmailService.Clear();

        // The window-opened recorders are deliberately NOT cleared here, unlike the two above. Those bags are
        // static, and this is the second class to write into them (CollectionWindowOpenedNotificationTests is the
        // first) — xUnit runs two classes at a time, so a Clear() from either would wipe the other's records
        // mid-test. Every assertion filters on a collection name unique to its test, which is the isolation that
        // actually holds; clearing was only ever belt-and-braces, and here it is the opposite.

        Context.CollectionReminderDispatches.RemoveRange(Context.CollectionReminderDispatches);
        Context.MaturityAssessments.RemoveRange(Context.MaturityAssessments);
        Context.Submissions.RemoveRange(Context.Submissions);
        Context.Collections.RemoveRange(Context.Collections);
        Context.AuditLogs.RemoveRange(Context.AuditLogs);
        await SaveChangesAsync();

        AuthenticateAsAdmin();
    }
}