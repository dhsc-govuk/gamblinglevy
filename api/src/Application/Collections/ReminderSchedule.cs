using Application.Common.Validation;
using Domain.Collections;

namespace Application.Collections;

// A reminder that is due: the calendar date it was due on, plus which configured reminder resolved to it.
public readonly record struct DueReminder(DateOnly ScheduledFor, string OffsetType, int Days);

// Turns a collection's configured reminder cadence (collection_reminders, written by the setup wizard) into the
// calendar dates a reminder is actually owed on (BLUM-7571).
//
// Pure and clock-free — "today" is always a parameter — for the same reason as ReportingPeriod alongside it: this
// is the part of the feature with all the edge cases, and a unit test costs nothing while an integration test
// costs a PostgreSQL container.
public static class ReminderSchedule
{
    // The calendar date a single configured reminder resolves to, or null if it cannot resolve.
    //
    // Null rather than a throw on a bad day count: the value is bounded by the create/update validators and by
    // CollectionReminder.Create, but a row restored from a backup or edited by hand is not, and DateOnly.AddDays
    // throws past DateOnly.MaxValue. One bad row must not take the whole sweep down with it.
    public static DateOnly? FireDate(ReminderOffsetType offsetType, DateOnly? openDate, DateOnly? deadline, int days)
    {
        if (offsetType is null || days < 0 || days > FieldLimits.MaxReminderDays)
        {
            return null;
        }

        // ReminderOffsetType is a ValueObject with no operator==, so compare by value via Equals — == would be
        // reference equality and never match a freshly-built ReminderOffsetType.AfterOpen.
        if (offsetType.Equals(ReminderOffsetType.AfterOpen))
        {
            return openDate?.AddDays(days);
        }

        return deadline?.AddDays(-days);
    }

    // The calendar date this collection's window-open notification is owed on, or null if none is (BLUM-7721).
    //
    // Deliberately NOT derived from collection_reminders. AC1/AC2 are unconditional — the window opening is owed
    // for every published collection, and an admin who configured no cadence has still opened a window. That is
    // why this is a sibling of DueOn rather than another offset type: an offset type would only fire for
    // collections whose setup happened to include it.
    //
    // It takes no maxAgeDays, and that is the one place it deliberately does NOT mirror DueOn. DueOn's catch-up
    // floor bounds a fire date derived from a cadence an admin set relative to "now", so a date far in the past
    // means something went wrong. CollectionOpenDate is not that: it is a business date that is routinely already
    // weeks old when the collection is published, because CreateCollectionCommand sets it to the reporting
    // period's start whenever WindowSameAsReportingPeriod is true and no validator forbids a past open date. A
    // floor here would silently skip the most ordinary setup there is — publish the Q1 collection a fortnight
    // into Q1 — with no email, no dispatch row and no log line.
    //
    // Nothing is lost by dropping it, because the claim this feeds is once per COLLECTION and permanent (see
    // ix_collection_reminder_dispatches_window_opened), not once per date. The deploy backfill claims every
    // already-open window, and the SQL prefilter excludes closed ones, so the worst an emptied dispatch table can
    // produce is one announcement per currently-open window — bounded, and true when it arrives.
    public static DateOnly? WindowOpenedOn(Collection collection, DateOnly today)
    {
        if (collection is null || collection.IsDraft || collection.IsDeleted)
        {
            return null;
        }

        // A collection published before the window fields were mandatory can be missing any of these. ACs 3 and 4
        // put the deadline AND the reporting period in the body, and an email saying "1 January 0001 to
        // 1 January 0001" is worse than no email; without an open date there is no honest answer to "when did it
        // open" at all. The same guard DueOn and CollectionClosureNotifier apply.
        if (collection.CollectionOpenDate is not { } openDate
            || collection.SubmissionDeadline is not { } deadline
            || collection.ReportingPeriodStart is null
            || collection.ReportingPeriodEnd is null)
        {
            return null;
        }

        if (openDate > today)
        {
            return null;
        }

        // Collection.ValidateWindow forbids it, but a row restored from a backup or edited by hand is not bound
        // by it, and "open after it shuts" is not a window.
        return openDate > deadline ? null : openDate;
    }

    // Every configured reminder for this collection whose fire date has been reached as at `today`, oldest first
    // and de-duplicated by date.
    //
    // The list is returned whole (rather than just the newest) so each filter below is assertable in isolation;
    // the caller decides how many of them to act on. CollectionReminderNotifier takes the LAST one, so a service
    // that was unloaded across two reminder dates sends the more urgent one rather than two identical emails in
    // the same minute.
    public static IReadOnlyList<DueReminder> DueOn(Collection collection, DateOnly today, int maxAgeDays)
    {
        if (collection is null || collection.IsDraft || collection.IsDeleted || collection.Reminders.Count == 0)
        {
            return [];
        }

        // AC2 requires the deadline date in the email body. A published collection always has one, but a draft
        // published before those fields were mandatory could not, and an email saying "by 01/01/0001" is worse
        // than no email — the same guard CollectionClosureNotifier applies to a missing reporting period.
        if (collection.SubmissionDeadline is null)
        {
            return [];
        }

        var deadline = collection.SubmissionDeadline.Value;
        var earliest = today.AddDays(-Math.Max(0, maxAgeDays));

        return collection.Reminders
            .Select(reminder => (
                Reminder: reminder,
                FireDate: FireDate(reminder.OffsetType, collection.CollectionOpenDate, collection.SubmissionDeadline, reminder.Days)))
            .Where(x => x.FireDate is not null)
            .Select(x => (x.Reminder, FireDate: x.FireDate!.Value))
            .Where(x => x.FireDate <= today)

            // The catch-up floor. App Service runs with always_on = false, so the site unloads after ~20 minutes
            // idle and the sweep only runs when somebody wakes it — a reminder configured for a day the service
            // never ran still has to go out. This bounds how far back that reaches, so a restore from backup or
            // a data reset cannot mail every LA once per historical fire date.
            .Where(x => x.FireDate > earliest)

            // An after_open offset longer than the window resolves after the window shut. Reminding somebody to
            // submit to a closed collection is not a reminder — post-deadline chasing is BLUM-7572.
            .Where(x => x.FireDate <= deadline)

            // A before_deadline offset longer than the window resolves before anyone could submit at all.
            // >= not >, deliberately: an admin may configure "0 days after the collection opens", which lands
            // exactly on the open date and is a legitimate reminder that must survive this filter. BLUM-7721's
            // window-open announcement is NOT that reminder — it is its own trigger, owed whether or not any
            // cadence was configured (see WindowOpenedOn above) — but the two land on the same day, and
            // CollectionReminderNotifier is what decides the announcement wins.
            .Where(x => collection.CollectionOpenDate is null || x.FireDate >= collection.CollectionOpenDate)

            .OrderBy(x => x.FireDate)
            .ThenBy(x => x.Reminder.OffsetType.ToString(), StringComparer.Ordinal)
            .ThenBy(x => x.Reminder.Days)

            // Two configured reminders can legitimately land on the same day (after_open 20 and
            // before_deadline 10 on a 30-day window). That is one email, not two. The ordering above makes which
            // reminder is recorded as the provenance deterministic rather than dependent on row order.
            .GroupBy(x => x.FireDate)
            .Select(group => group.First())
            .Select(x => new DueReminder(x.FireDate, x.Reminder.OffsetType.ToString(), x.Reminder.Days))
            .ToList();
    }
}