using Domain.Collections;
using Domain.DataCollectionForms;
using Domain.Users;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Unit.Collections;

public class CollectionTests
{
    // A fixed instant. GetStatus is judged against the time it is handed (BLUM-8753), so these do not depend
    // on the day the suite happens to run and the boundary days can be stated outright.
    private static readonly DateTime Now = new(2026, 8, 4, 9, 0, 0, DateTimeKind.Utc);

    private static readonly DateOnly Today = DateOnly.FromDateTime(Now);

    private static Collection Build(bool saveAsDraft, DateOnly openDate, DateOnly deadline) =>
        Collection.New(
            CollectionId.New(),
            "2026 Q1 Quarterly report",
            "purpose",
            CollectionType.QuarterlyReport,
            "2026",
            1,
            new DateOnly(2026, 4, 1),
            new DateOnly(2026, 6, 30),
            windowSameAsReportingPeriod: false,
            openDate,
            deadline,
            isSubmittedByAllLas: true,
            UserId.New(),
            saveAsDraft,
            utcNow: Now);

    [Fact]
    public void New_AsDraft_DerivesDraftStatusRegardlessOfDates()
    {
        var collection = Build(saveAsDraft: true, Today.AddDays(-10), Today.AddDays(-5));

        collection.IsDraft.Should().BeTrue();
        collection.GetStatus(Now).ToString().Should().Be(CollectionStatuses.Draft);
    }

    [Fact]
    public void GetStatus_PublishedBeforeOpenDate_IsScheduled()
    {
        var collection = Build(saveAsDraft: false, Today.AddDays(5), Today.AddDays(10));

        collection.GetStatus(Now).ToString().Should().Be(CollectionStatuses.Scheduled);
    }

    [Fact]
    public void GetStatus_WithinWindow_IsOpen()
    {
        var collection = Build(saveAsDraft: false, Today.AddDays(-5), Today.AddDays(5));

        collection.GetStatus(Now).ToString().Should().Be(CollectionStatuses.Open);
    }

    [Fact]
    public void GetStatus_PastDeadline_IsClosed()
    {
        var collection = Build(saveAsDraft: false, Today.AddDays(-10), Today.AddDays(-5));

        collection.GetStatus(Now).ToString().Should().Be(CollectionStatuses.Closed);
    }

    [Theory]
    [InlineData(-1, CollectionStatuses.Scheduled)]
    [InlineData(0, CollectionStatuses.Open)]
    [InlineData(5, CollectionStatuses.Open)]
    [InlineData(6, CollectionStatuses.Closed)]
    public void GetStatus_TreatsBothEndsOfTheWindowAsOpen(int daysAfterOpenDate, string expected)
    {
        // Opens on Today with the deadline five days later. Both boundary days are inclusive: the window is
        // open on its first day and still open on the deadline itself, closing the day after.
        var collection = Build(saveAsDraft: false, Today, Today.AddDays(5));

        collection.GetStatus(Now.AddDays(daysAfterOpenDate)).ToString().Should().Be(expected);
    }

    [Fact]
    public void GetStatus_StaysOpenUntilTheDeadlineDayEndsInUtc()
    {
        var deadline = Today.AddDays(5);
        var collection = Build(saveAsDraft: false, Today, deadline);
        var lastSecond = deadline.ToDateTime(new TimeOnly(23, 59, 59), DateTimeKind.Utc);

        collection.GetStatus(lastSecond).ToString().Should().Be(CollectionStatuses.Open);
        collection.GetStatus(lastSecond.AddSeconds(1)).ToString().Should().Be(CollectionStatuses.Closed);
    }

    [Fact]
    public void Publish_TurnsDraftIntoDatedStatus()
    {
        var collection = Build(saveAsDraft: true, Today.AddDays(5), Today.AddDays(10));

        collection.Publish(UserId.New(), Now);

        collection.IsDraft.Should().BeFalse();
        collection.GetStatus(Now).ToString().Should().Be(CollectionStatuses.Scheduled);
    }

    [Fact]
    public void Close_ForcesClosedStatusEvenWithinWindow()
    {
        var collection = Build(saveAsDraft: false, Today.AddDays(-5), Today.AddDays(5));

        collection.Close(UserId.New(), Now);

        collection.GetStatus(Now).ToString().Should().Be(CollectionStatuses.Closed);
    }

    [Fact]
    public void RevertToDraft_ReturnsToDraftAndClearsManualClose()
    {
        var collection = Build(saveAsDraft: false, Today.AddDays(-5), Today.AddDays(5));
        collection.Close(UserId.New(), Now);

        collection.RevertToDraft(UserId.New(), Now);

        collection.IsDraft.Should().BeTrue();
        collection.GetStatus(Now).ToString().Should().Be(CollectionStatuses.Draft);
    }

    [Fact]
    public void MarkClosureNotified_RecordsTheStamp_WithoutTouchingTheAuditFields()
    {
        var collection = Build(saveAsDraft: false, Today.AddDays(-10), Today.AddDays(-5));

        collection.MarkClosureNotified(Now);

        collection.ClosureNotifiedAt.Should().NotBeNull();

        // The sender is a background sweep with nobody behind it, so stamping UpdatedBy/UpdatedAt would make the
        // next CollectionSnapshot read as an admin edit that never happened (BLUM-7719).
        collection.UpdatedBy.Should().BeNull();
        collection.UpdatedAt.Should().BeNull();
    }

    [Fact]
    public void RevertToDraft_ClearsTheClosureNotificationStamp()
    {
        var collection = Build(saveAsDraft: false, Today.AddDays(-5), Today.AddDays(5));
        collection.Close(UserId.New(), Now);
        collection.MarkClosureNotified(Now);

        collection.RevertToDraft(UserId.New(), Now);

        // The stamp means "notified for the closure currently in effect". Reopening ends that closure, so the
        // notification is owed again next time the window closes.
        collection.ClosureNotifiedAt.Should().BeNull();
    }

    [Fact]
    public void SoftDelete_ThenRestore_TogglesDeletedFlag()
    {
        var collection = Build(saveAsDraft: true, Today, Today.AddDays(5));

        collection.SoftDelete(UserId.New(), Now);
        collection.IsDeleted.Should().BeTrue();

        collection.Restore(UserId.New(), Now);
        collection.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void SoftDelete_WhenAlreadyDeleted_Throws()
    {
        var collection = Build(saveAsDraft: true, Today, Today.AddDays(5));
        collection.SoftDelete(UserId.New(), Now);

        var act = () => collection.SoftDelete(UserId.New(), Now);

        act.Should().Throw<CollectionAlreadyDeletedException>();
    }

    [Fact]
    public void Restore_WhenNotDeleted_Throws()
    {
        var collection = Build(saveAsDraft: true, Today, Today.AddDays(5));

        var act = () => collection.Restore(UserId.New(), Now);

        act.Should().Throw<CollectionNotDeletedException>();
    }

    [Fact]
    public void RevertToDraft_KeepsRecordedFormVersions()
    {
        var module = FormModuleId.New();
        var version = FormModuleVersionId.New();
        var collection = Build(saveAsDraft: false, Today.AddDays(-5), Today.AddDays(5));
        collection.SetForms(new[] { module });
        collection.PinFormVersions(new Dictionary<FormModuleId, FormModuleVersionId> { [module] = version });

        collection.RevertToDraft(UserId.New(), Now);

        // BLUM-9103: only an Open collection can be reverted, so its window has opened and its versions are fixed.
        collection.Forms.Single().FormModuleVersionId.Should().Be(version);
    }

    [Theory]
    [InlineData(true, 5, false)]
    [InlineData(false, 5, true)]
    [InlineData(false, 1, true)]
    [InlineData(false, 0, false)]
    [InlineData(false, -5, false)]
    public void FollowsLatestFormVersions_OnlyUntilTheWindowOpens(bool draft, int daysUntilOpen, bool expected)
    {
        // BLUM-9103: Scheduled follows the latest version; Draft never moves, and from the open date (including a window
        // that opens today) the versions are fixed.
        var collection = Build(saveAsDraft: draft, Today.AddDays(daysUntilOpen), Today.AddDays(10));

        collection.FollowsLatestFormVersions(Now).Should().Be(expected);
    }

    [Fact]
    public void FollowsLatestFormVersions_IsFalse_OnceClosed()
    {
        var collection = Build(saveAsDraft: false, Today.AddDays(5), Today.AddDays(10));
        collection.Close(UserId.New(), Now);

        collection.FollowsLatestFormVersions(Now).Should().BeFalse();
    }

    [Fact]
    public void FollowLatestFormVersions_MovesEveryRecordedVersion_AndLeavesUnrecordedFormsAlone()
    {
        var recorded = FormModuleId.New();
        var unrecorded = FormModuleId.New();
        var collection = Build(saveAsDraft: false, Today.AddDays(-5), Today.AddDays(5));
        collection.SetForms(new[] { recorded, unrecorded });
        var original = FormModuleVersionId.New();
        collection.PinFormVersions(new Dictionary<FormModuleId, FormModuleVersionId> { [recorded] = original });
        var latest = new Dictionary<FormModuleId, FormModuleVersionId>
        {
            [recorded] = FormModuleVersionId.New(),
            [unrecorded] = FormModuleVersionId.New(),
        };

        var moves = collection.FollowLatestFormVersions(latest);

        moves.Should().Equal(new FormVersionMove(recorded, original, latest[recorded]));
        var forms = collection.Forms.ToDictionary(f => f.FormModuleId);
        forms[recorded].FormModuleVersionId.Should().Be(latest[recorded]);
        forms[recorded].IsVersionChosen.Should().BeFalse();
        forms[unrecorded].FormModuleVersionId.Should().BeNull("linking an unrecorded form is the backfill's job");
    }

    [Fact]
    public void FollowLatestFormVersions_MovesNothing_WhenEachRecordedVersionIsTheLatest()
    {
        var module = FormModuleId.New();
        var version = FormModuleVersionId.New();
        var collection = Build(saveAsDraft: false, Today.AddDays(-5), Today.AddDays(5));
        collection.SetForms(new[] { module });
        collection.PinFormVersions(new Dictionary<FormModuleId, FormModuleVersionId> { [module] = version });

        collection.FollowLatestFormVersions(new Dictionary<FormModuleId, FormModuleVersionId> { [module] = FormModuleVersionId.From(version.Value) })
            .Should().BeEmpty();
    }

    [Fact]
    public void SetForms_And_SetReminders_ReplaceSatelliteCollections()
    {
        var collection = Build(saveAsDraft: true, Today, Today.AddDays(5));

        collection.SetForms(new[] { FormModuleId.New(), FormModuleId.New() });
        collection.SetReminders(new[]
        {
            (ReminderOffsetType.AfterOpen, 3),
            (ReminderOffsetType.BeforeDeadline, 7),
        }, Now);

        collection.Forms.Should().HaveCount(2);
        collection.Reminders.Should().HaveCount(2);
    }
}