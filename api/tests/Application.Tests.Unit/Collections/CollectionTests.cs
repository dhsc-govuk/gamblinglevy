using Domain.Collections;
using Domain.DataCollectionForms;
using Domain.Users;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Unit.Collections;

public class CollectionTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

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
            saveAsDraft);

    [Fact]
    public void New_AsDraft_DerivesDraftStatusRegardlessOfDates()
    {
        var collection = Build(saveAsDraft: true, Today.AddDays(-10), Today.AddDays(-5));

        collection.IsDraft.Should().BeTrue();
        collection.GetStatus().ToString().Should().Be(CollectionStatuses.Draft);
    }

    [Fact]
    public void GetStatus_PublishedBeforeOpenDate_IsScheduled()
    {
        var collection = Build(saveAsDraft: false, Today.AddDays(5), Today.AddDays(10));

        collection.GetStatus().ToString().Should().Be(CollectionStatuses.Scheduled);
    }

    [Fact]
    public void GetStatus_WithinWindow_IsOpen()
    {
        var collection = Build(saveAsDraft: false, Today.AddDays(-5), Today.AddDays(5));

        collection.GetStatus().ToString().Should().Be(CollectionStatuses.Open);
    }

    [Fact]
    public void GetStatus_PastDeadline_IsClosed()
    {
        var collection = Build(saveAsDraft: false, Today.AddDays(-10), Today.AddDays(-5));

        collection.GetStatus().ToString().Should().Be(CollectionStatuses.Closed);
    }

    [Fact]
    public void Publish_TurnsDraftIntoDatedStatus()
    {
        var collection = Build(saveAsDraft: true, Today.AddDays(5), Today.AddDays(10));

        collection.Publish(UserId.New());

        collection.IsDraft.Should().BeFalse();
        collection.GetStatus().ToString().Should().Be(CollectionStatuses.Scheduled);
    }

    [Fact]
    public void Close_ForcesClosedStatusEvenWithinWindow()
    {
        var collection = Build(saveAsDraft: false, Today.AddDays(-5), Today.AddDays(5));

        collection.Close(UserId.New());

        collection.GetStatus().ToString().Should().Be(CollectionStatuses.Closed);
    }

    [Fact]
    public void RevertToDraft_ReturnsToDraftAndClearsManualClose()
    {
        var collection = Build(saveAsDraft: false, Today.AddDays(-5), Today.AddDays(5));
        collection.Close(UserId.New());

        collection.RevertToDraft(UserId.New());

        collection.IsDraft.Should().BeTrue();
        collection.GetStatus().ToString().Should().Be(CollectionStatuses.Draft);
    }

    [Fact]
    public void SoftDelete_ThenRestore_TogglesDeletedFlag()
    {
        var collection = Build(saveAsDraft: true, Today, Today.AddDays(5));

        collection.SoftDelete(UserId.New());
        collection.IsDeleted.Should().BeTrue();

        collection.Restore(UserId.New());
        collection.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void SoftDelete_WhenAlreadyDeleted_Throws()
    {
        var collection = Build(saveAsDraft: true, Today, Today.AddDays(5));
        collection.SoftDelete(UserId.New());

        var act = () => collection.SoftDelete(UserId.New());

        act.Should().Throw<CollectionAlreadyDeletedException>();
    }

    [Fact]
    public void Restore_WhenNotDeleted_Throws()
    {
        var collection = Build(saveAsDraft: true, Today, Today.AddDays(5));

        var act = () => collection.Restore(UserId.New());

        act.Should().Throw<CollectionNotDeletedException>();
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
        });

        collection.Forms.Should().HaveCount(2);
        collection.Reminders.Should().HaveCount(2);
    }
}