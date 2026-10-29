using Application.Collections;
using Application.Common.Validation;
using Domain.Collections;
using Domain.Users;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Unit.Collections;

public class ReminderScheduleTests
{
    private static readonly DateOnly Open = new(2026, 9, 1);
    private static readonly DateOnly Deadline = new(2026, 9, 30);

    [Theory]
    [InlineData(0, 2026, 9, 1)]
    [InlineData(1, 2026, 9, 2)]
    [InlineData(14, 2026, 9, 15)]
    public void FireDate_AfterOpen_AddsDaysToTheOpenDate(int days, int year, int month, int day)
    {
        var result = ReminderSchedule.FireDate(ReminderOffsetType.AfterOpen, Open, Deadline, days);

        result.Should().Be(new DateOnly(year, month, day));
    }

    [Theory]
    [InlineData(0, 2026, 9, 30)]
    [InlineData(7, 2026, 9, 23)]
    [InlineData(29, 2026, 9, 1)]
    public void FireDate_BeforeDeadline_SubtractsDaysFromTheDeadline(int days, int year, int month, int day)
    {
        var result = ReminderSchedule.FireDate(ReminderOffsetType.BeforeDeadline, Open, Deadline, days);

        result.Should().Be(new DateOnly(year, month, day));
    }

    [Fact]
    public void FireDate_ReturnsNull_WhenTheAnchorDateIsMissing()
    {
        ReminderSchedule.FireDate(ReminderOffsetType.AfterOpen, null, Deadline, 3).Should().BeNull();
        ReminderSchedule.FireDate(ReminderOffsetType.BeforeDeadline, Open, null, 3).Should().BeNull();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(FieldLimits.MaxReminderDays + 1)]
    public void FireDate_ReturnsNull_ForAnOutOfRangeDayCount(int days)
    {
        // The validators and CollectionReminder.Create bound this, but a row restored from a backup or edited by
        // hand is not bounded — and DateOnly.AddDays throws past DateOnly.MaxValue. One bad row must not take the
        // whole sweep down with it.
        ReminderSchedule.FireDate(ReminderOffsetType.AfterOpen, Open, Deadline, days).Should().BeNull();
        ReminderSchedule.FireDate(ReminderOffsetType.BeforeDeadline, Open, Deadline, days).Should().BeNull();
    }

    [Fact]
    public void DueOn_ReturnsTheReminderThatHasFallenDue()
    {
        var collection = CollectionWith((ReminderOffsetType.BeforeDeadline, 7));

        var due = ReminderSchedule.DueOn(collection, new DateOnly(2026, 9, 23), maxAgeDays: 7);

        due.Should().ContainSingle();
        due[0].ScheduledFor.Should().Be(new DateOnly(2026, 9, 23));
        due[0].OffsetType.Should().Be(ReminderOffsetTypes.BeforeDeadline);
        due[0].Days.Should().Be(7);
    }

    [Fact]
    public void DueOn_ReturnsNothing_BeforeTheFireDate()
    {
        var collection = CollectionWith((ReminderOffsetType.BeforeDeadline, 7));

        ReminderSchedule.DueOn(collection, new DateOnly(2026, 9, 22), maxAgeDays: 7).Should().BeEmpty();
    }

    [Fact]
    public void DueOn_IncludesAMissedFireDateWithinTheCatchUpWindow()
    {
        // App Service runs with always_on = false, so the sweep only runs when something wakes the site. A
        // reminder configured for a day nothing ran must still go out.
        var collection = CollectionWith((ReminderOffsetType.BeforeDeadline, 14));

        var due = ReminderSchedule.DueOn(collection, new DateOnly(2026, 9, 20), maxAgeDays: 7);

        due.Should().ContainSingle().Which.ScheduledFor.Should().Be(new DateOnly(2026, 9, 16));
    }

    [Fact]
    public void DueOn_ExcludesAFireDateOlderThanTheMaxAge()
    {
        // The backstop behind the deploy backfill: without it, anything that empties
        // collection_reminder_dispatches would mail every outstanding organisation once per historical fire date.
        var collection = CollectionWith((ReminderOffsetType.BeforeDeadline, 14));

        ReminderSchedule.DueOn(collection, new DateOnly(2026, 9, 25), maxAgeDays: 7).Should().BeEmpty();
    }

    [Fact]
    public void DueOn_DropsAnAfterOpenOffsetThatLandsPastTheDeadline()
    {
        // A "you have not submitted" nudge after the window shut is not a reminder — post-deadline chasing is
        // BLUM-7572's escalation.
        var collection = CollectionWith((ReminderOffsetType.AfterOpen, 40));

        ReminderSchedule.DueOn(collection, new DateOnly(2026, 10, 11), maxAgeDays: 30).Should().BeEmpty();
    }

    [Fact]
    public void DueOn_DropsABeforeDeadlineOffsetThatLandsBeforeTheOpenDate()
    {
        // 60 days before a 30 September deadline is 1 August — before anybody could submit at all.
        var collection = CollectionWith((ReminderOffsetType.BeforeDeadline, 60));

        ReminderSchedule.DueOn(collection, new DateOnly(2026, 9, 5), maxAgeDays: 60).Should().BeEmpty();
    }

    [Fact]
    public void DueOn_KeepsAnAfterOpenZeroOnTheOpenDate()
    {
        // An admin may configure "0 days after the collection opens", which lands exactly on the open date. This
        // pins the >= in the pre-open filter against a "tidy" change to >. The reminder still RESOLVES here even
        // when BLUM-7721's window-open announcement covers the same day — dropping it is the notifier's decision,
        // and it claims the trigger point rather than leaving it due (see
        // CollectionReminderNotificationTests.ShouldSendOnlyTheWindowOpenEmail_WhenAReminderLandsOnTheOpenDate).
        var collection = CollectionWith((ReminderOffsetType.AfterOpen, 0));

        var due = ReminderSchedule.DueOn(collection, Open, maxAgeDays: 7);

        due.Should().ContainSingle().Which.ScheduledFor.Should().Be(Open);
    }

    [Fact]
    public void DueOn_DeduplicatesTwoRemindersLandingOnTheSameDay()
    {
        // after_open 22 and before_deadline 7 both resolve to 23 September on a 1–30 September window. That is
        // one email, not two.
        var collection = CollectionWith(
            (ReminderOffsetType.AfterOpen, 22),
            (ReminderOffsetType.BeforeDeadline, 7));

        var due = ReminderSchedule.DueOn(collection, new DateOnly(2026, 9, 23), maxAgeDays: 7);

        due.Should().ContainSingle().Which.ScheduledFor.Should().Be(new DateOnly(2026, 9, 23));
    }

    [Fact]
    public void DueOn_OrdersAscending_SoTheCallerCanTakeTheLatest()
    {
        // CollectionReminderNotifier takes LastOrDefault() to send only the most urgent missed reminder; this is
        // the contract that rests on.
        var collection = CollectionWith(
            (ReminderOffsetType.BeforeDeadline, 14),
            (ReminderOffsetType.BeforeDeadline, 10));

        var due = ReminderSchedule.DueOn(collection, new DateOnly(2026, 9, 21), maxAgeDays: 14);

        due.Select(x => x.ScheduledFor).Should().ContainInOrder(new DateOnly(2026, 9, 16), new DateOnly(2026, 9, 20));
        due.Last().Days.Should().Be(10);
    }

    [Fact]
    public void DueOn_ReturnsNothing_WhenThereIsNoSubmissionDeadline()
    {
        // AC2 needs the deadline in the email body; "by 01/01/0001" is worse than no email.
        var collection = Collection.New(
            CollectionId.New(), "No deadline", null, CollectionType.QuarterlyReport, "2026", 2,
            new DateOnly(2026, 7, 1), new DateOnly(2026, 9, 30), false, Open, null, true,
            UserId.New(), saveAsDraft: true,
            utcNow: DateTime.UtcNow);
        collection.SetReminders([(ReminderOffsetType.AfterOpen, 1)], DateTime.UtcNow);

        ReminderSchedule.DueOn(collection, new DateOnly(2026, 9, 23), maxAgeDays: 7).Should().BeEmpty();
    }

    [Fact]
    public void DueOn_ReturnsNothing_ForADraft()
    {
        var collection = CollectionWith(saveAsDraft: true, reminders: (ReminderOffsetType.BeforeDeadline, 7));

        ReminderSchedule.DueOn(collection, new DateOnly(2026, 9, 23), maxAgeDays: 7).Should().BeEmpty();
    }

    [Fact]
    public void DueOn_ReturnsNothing_WhenNoRemindersAreConfigured()
    {
        var collection = CollectionWith();

        ReminderSchedule.DueOn(collection, new DateOnly(2026, 9, 23), maxAgeDays: 7).Should().BeEmpty();
    }

    // ---------------------------------------------------------------------------------------------------------
    // WindowOpenedOn (BLUM-7721)
    // ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void WindowOpenedOn_ReturnsTheOpenDate_OnTheDayItOpens()
    {
        ReminderSchedule.WindowOpenedOn(CollectionWith(), Open).Should().Be(Open);
    }

    [Fact]
    public void WindowOpenedOn_ReturnsTheOpenDate_WhenNoRemindersAreConfigured()
    {
        // The whole difference from DueOn, and the reason ICollectionQueries stopped filtering on
        // Reminders.Any(). ACs 1 and 2 are unconditional: an admin who set no cadence up has still opened a
        // window. CollectionWith() takes no reminders, so this is exactly that collection.
        CollectionWith().Reminders.Should().BeEmpty();

        ReminderSchedule.WindowOpenedOn(CollectionWith(), Open).Should().Be(Open);
    }

    [Fact]
    public void WindowOpenedOn_ReturnsTheOpenDate_HoweverLongAgoTheWindowOpened()
    {
        // Deliberately NOT floored the way DueOn is, and this is the fact that says so. CollectionOpenDate is a
        // business date: CreateCollectionCommand sets it to the reporting period's start whenever
        // WindowSameAsReportingPeriod is true, and no validator forbids a past open date — so publishing the Q1
        // collection a fortnight into Q1 is ordinary, not a fault. A catch-up floor here would announce nothing
        // at all for the commonest setup there is, silently.
        //
        // Nothing guards against repeats in this method because nothing needs to: the claim it feeds is once per
        // COLLECTION and permanent, unlike a reminder's once-per-trigger-point.
        ReminderSchedule.WindowOpenedOn(CollectionWith(), Open.AddDays(6)).Should().Be(Open);
        ReminderSchedule.WindowOpenedOn(CollectionWith(), Open.AddDays(21)).Should().Be(Open);
    }

    [Fact]
    public void WindowOpenedOn_ReturnsNull_BeforeTheWindowOpens()
    {
        ReminderSchedule.WindowOpenedOn(CollectionWith(), Open.AddDays(-1)).Should().BeNull();
    }

    [Fact]
    public void WindowOpenedOn_ReturnsNull_ForADraftOrDeletedCollection()
    {
        ReminderSchedule.WindowOpenedOn(CollectionWith(saveAsDraft: true), Open).Should().BeNull();

        var deleted = CollectionWith();
        deleted.SoftDelete(UserId.New(), DateTime.UtcNow);

        ReminderSchedule.WindowOpenedOn(deleted, Open).Should().BeNull();
    }

    [Fact]
    public void WindowOpenedOn_ReturnsNull_WhenTheWindowOrPeriodIsIncomplete()
    {
        // A draft published before those fields were mandatory can be missing any of them, and ACs 3 and 4 put
        // the deadline AND the reporting period in the body — "1 January 0001" is worse than no email. Each arm
        // nulls exactly one field so none of them can pass for another's reason.
        var periodStart = new DateOnly(2026, 7, 1);
        var periodEnd = new DateOnly(2026, 9, 30);

        ReminderSchedule.WindowOpenedOn(Published(null, Deadline, periodStart, periodEnd), Open).Should().BeNull();
        ReminderSchedule.WindowOpenedOn(Published(Open, null, periodStart, periodEnd), Open).Should().BeNull();
        ReminderSchedule.WindowOpenedOn(Published(Open, Deadline, null, periodEnd), Open).Should().BeNull();
        ReminderSchedule.WindowOpenedOn(Published(Open, Deadline, periodStart, null), Open).Should().BeNull();

        // The same builder with everything answered still resolves, so the four nulls above are what did it.
        ReminderSchedule.WindowOpenedOn(Published(Open, Deadline, periodStart, periodEnd), Open).Should().Be(Open);
    }

    // A collection whose window fields are not all answered. Only a draft may be built that way — each of these
    // stands in for a legacy row published before those fields became mandatory, which the sweep still has to
    // survive — so it is published afterwards to reach the non-draft branch of WindowOpenedOn.
    private static Collection Published(
        DateOnly? openDate,
        DateOnly? deadline,
        DateOnly? periodStart,
        DateOnly? periodEnd)
    {
        var collection = Collection.New(
            CollectionId.New(),
            "Incomplete window fixture",
            null,
            CollectionType.QuarterlyReport,
            "2026",
            2,
            periodStart,
            periodEnd,
            false,
            openDate,
            deadline,
            true,
            UserId.New(),
            saveAsDraft: true,
            utcNow: DateTime.UtcNow);

        collection.Publish(UserId.New(), DateTime.UtcNow);
        return collection;
    }

    private static Collection CollectionWith(params (ReminderOffsetType Offset, int Days)[] reminders)
        => CollectionWith(saveAsDraft: false, reminders);

    private static Collection CollectionWith(bool saveAsDraft, params (ReminderOffsetType Offset, int Days)[] reminders)
    {
        var collection = Collection.New(
            CollectionId.New(),
            "Reminder schedule fixture",
            null,
            CollectionType.QuarterlyReport,
            "2026",
            2,
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 9, 30),
            false,
            Open,
            Deadline,
            true,
            UserId.New(),
            saveAsDraft,
            DateTime.UtcNow);

        collection.SetReminders(reminders, DateTime.UtcNow);
        return collection;
    }
}