using Domain.Common;
using Domain.DataCollectionForms;
using Domain.Organisations;
using Domain.Users;

namespace Domain.Collections;

// A data-collection round: a window during which assigned Local Authorities submit a form (a quarterly report or
// maturity assessment) covering a reporting period. Setup can be saved part-way ("Save as draft", IsDraft == true);
// a draft only requires a Name — every other setup question may still be unanswered (null), so none of those fields
// may be defaulted: null means "not answered yet", not "No". Completeness is enforced when the collection stops
// being a draft. The published collection's lifecycle Status (Draft/Scheduled/Open/Closed) is DERIVED — see
// GetStatus() — from the draft/manually-closed flags and the open/deadline dates relative to today. Delete is a
// soft-delete (DeletedAt) so records can be restored. Forms, Local Authorities and reminders are id-only reference
// satellites.
public class Collection : IEntity<CollectionId>
{
    public CollectionId Id { get; private set; }
    public string Name { get; private set; }
    public string? Purpose { get; private set; }
    public CollectionType? Type { get; private set; }
    public string? FinancialYear { get; private set; }
    public int? Quarter { get; private set; }
    public DateOnly? ReportingPeriodStart { get; private set; }
    public DateOnly? ReportingPeriodEnd { get; private set; }
    public bool? WindowSameAsReportingPeriod { get; private set; }
    public DateOnly? CollectionOpenDate { get; private set; }
    public DateOnly? SubmissionDeadline { get; private set; }
    public bool? IsSubmittedByAllLas { get; private set; }
    public bool IsDraft { get; private set; }
    public bool IsManuallyClosed { get; private set; }

    // When Close() was called (BLUM-8155). A manual close makes GetStatus() report Closed immediately, independent
    // of SubmissionDeadline — so the date this window actually closed cannot be derived from the deadline alone
    // once a manual close happens before the deadline. See CollectionWindowClosedLaNotifier.GetNotificationDueDate.
    public DateTime? ManuallyClosedAt { get; private set; }

    // When the DHSC closure notification for this window was sent (BLUM-7719). Bookkeeping for the notifier,
    // not user-facing state: closure itself is derived (GetStatus()), so this is the only record that the
    // one-off email has already gone out, and the only thing stopping the sweep sending it again on every run.
    public DateTime? ClosureNotifiedAt { get; private set; }

    // When the "reporting window closed" email to LA users of non-submitting organisations was sent (BLUM-8155).
    // Same bookkeeping role as ClosureNotifiedAt, but a separate marker: different audience (LA users, not DHSC
    // admins) and different timing (fires a day after close, not immediately), so the two must be able to be
    // notified independently.
    public DateTime? WindowClosedLaNotifiedAt { get; private set; }
    public UserId CreatedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public UserId? UpdatedBy { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public DateTime? DeletedAt { get; private set; }
    public UserId? DeletedBy { get; private set; }

    private readonly List<CollectionForm> _forms = new();
    public IReadOnlyCollection<CollectionForm> Forms => _forms.AsReadOnly();

    private readonly List<CollectionLocalAuthority> _localAuthorities = new();
    public IReadOnlyCollection<CollectionLocalAuthority> LocalAuthorities => _localAuthorities.AsReadOnly();

    private readonly List<CollectionReminder> _reminders = new();
    public IReadOnlyCollection<CollectionReminder> Reminders => _reminders.AsReadOnly();

    private Collection(
        CollectionId id,
        string name,
        string? purpose,
        CollectionType? type,
        string? financialYear,
        int? quarter,
        DateOnly? reportingPeriodStart,
        DateOnly? reportingPeriodEnd,
        bool? windowSameAsReportingPeriod,
        DateOnly? collectionOpenDate,
        DateOnly? submissionDeadline,
        bool? isSubmittedByAllLas,
        bool isDraft,
        UserId createdBy,
        DateTime createdAt)
    {
        Id = id;
        Name = name;
        Purpose = purpose;
        Type = type;
        FinancialYear = financialYear;
        Quarter = quarter;
        ReportingPeriodStart = reportingPeriodStart;
        ReportingPeriodEnd = reportingPeriodEnd;
        WindowSameAsReportingPeriod = windowSameAsReportingPeriod;
        CollectionOpenDate = collectionOpenDate;
        SubmissionDeadline = submissionDeadline;
        IsSubmittedByAllLas = isSubmittedByAllLas;
        IsDraft = isDraft;
        IsManuallyClosed = false;
        ManuallyClosedAt = null;
        ClosureNotifiedAt = null;
        WindowClosedLaNotifiedAt = null;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
        UpdatedBy = null;
        UpdatedAt = null;
        DeletedAt = null;
        DeletedBy = null;
    }

    public static Collection New(
        CollectionId id,
        string name,
        string? purpose,
        CollectionType? type,
        string? financialYear,
        int? quarter,
        DateOnly? periodStart,
        DateOnly? periodEnd,
        bool? windowSameAsReportingPeriod,
        DateOnly? openDate,
        DateOnly? deadline,
        bool? isSubmittedByAllLas,
        UserId createdBy,
        bool saveAsDraft,
        DateTime utcNow)
    {
        Guard.NotNull(id, nameof(id));
        Guard.NotNull(createdBy, nameof(createdBy));
        Guard.NotNullOrEmptyOrWhiteSpace(name, nameof(name));

        var normalisedQuarter = ValidateWindow(
            requireComplete: !saveAsDraft, type, financialYear, quarter, periodStart, periodEnd, windowSameAsReportingPeriod, openDate, deadline, isSubmittedByAllLas);

        return new Collection(
            id,
            name.Trim(),
            Normalise(purpose),
            type,
            Normalise(financialYear),
            normalisedQuarter,
            periodStart,
            periodEnd,
            windowSameAsReportingPeriod,
            openDate,
            deadline,
            isSubmittedByAllLas,
            saveAsDraft,
            createdBy,
            utcNow);
    }

    public void UpdateDetails(
        string name,
        string? purpose,
        CollectionType? type,
        string? financialYear,
        int? quarter,
        DateOnly? periodStart,
        DateOnly? periodEnd,
        bool? windowSameAsReportingPeriod,
        DateOnly? openDate,
        DateOnly? deadline,
        bool? isSubmittedByAllLas,
        UserId actor,
        bool saveAsDraft,
        DateTime utcNow)
    {
        EnsureEditable(utcNow);
        Guard.NotNull(actor, nameof(actor));
        Guard.NotNullOrEmptyOrWhiteSpace(name, nameof(name));

        // Only a collection that is and stays a draft may keep unanswered questions; anything that is
        // (or is about to become) published must be complete.
        var normalisedQuarter = ValidateWindow(
            requireComplete: !(IsDraft && saveAsDraft), type, financialYear, quarter, periodStart, periodEnd, windowSameAsReportingPeriod, openDate, deadline, isSubmittedByAllLas);

        Name = name.Trim();
        Purpose = Normalise(purpose);
        Type = type;
        FinancialYear = Normalise(financialYear);
        Quarter = normalisedQuarter;
        ReportingPeriodStart = periodStart;
        ReportingPeriodEnd = periodEnd;
        WindowSameAsReportingPeriod = windowSameAsReportingPeriod;
        CollectionOpenDate = openDate;
        SubmissionDeadline = deadline;
        IsSubmittedByAllLas = isSubmittedByAllLas;
        UpdatedBy = actor;
        UpdatedAt = utcNow;
    }

    public void Publish(UserId actor, DateTime utcNow)
    {
        Guard.NotNull(actor, nameof(actor));

        IsDraft = false;
        UpdatedBy = actor;
        UpdatedAt = utcNow;
    }

    public void RevertToDraft(UserId actor, DateTime utcNow)
    {
        Guard.NotNull(actor, nameof(actor));

        IsDraft = true;
        IsManuallyClosed = false;

        // BLUM-9103: the recorded form versions are kept. Only an Open collection can be reverted, so its window has
        // opened and its versions are fixed (FollowsLatestFormVersions); publishing it again must not move a report
        // that has started onto newer questions.
        ManuallyClosedAt = null;

        // Reopening un-closes the window, so both closure notifications are owed again the next time it closes.
        ClosureNotifiedAt = null;
        WindowClosedLaNotifiedAt = null;
        UpdatedBy = actor;
        UpdatedAt = utcNow;
    }

    public void Close(UserId actor, DateTime utcNow)
    {
        Guard.NotNull(actor, nameof(actor));

        IsManuallyClosed = true;
        ManuallyClosedAt = utcNow;
        UpdatedBy = actor;
        UpdatedAt = utcNow;
    }

    // Records that the closure notification has been sent. Deliberately takes no actor and does NOT touch
    // UpdatedBy/UpdatedAt: the sender is a background sweep with no human behind it, and stamping the audit
    // fields would make the next CollectionSnapshot read as an admin edit that never happened.
    public void MarkClosureNotified(DateTime utcNow)
    {
        ClosureNotifiedAt = utcNow;
    }

    public void SoftDelete(UserId actor, DateTime utcNow)
    {
        Guard.NotNull(actor, nameof(actor));
        if (IsDeleted)
        {
            throw new CollectionAlreadyDeletedException(Id);
        }

        DeletedAt = utcNow;
        DeletedBy = actor;
        UpdatedBy = actor;
        UpdatedAt = utcNow;
    }

    public void Restore(UserId actor, DateTime utcNow)
    {
        Guard.NotNull(actor, nameof(actor));
        if (!IsDeleted)
        {
            throw new CollectionNotDeletedException(Id);
        }

        DeletedAt = null;
        DeletedBy = null;
        UpdatedBy = actor;
        UpdatedAt = utcNow;
    }

    public void SetForms(IReadOnlyList<FormModuleId> moduleIds)
    {
        Guard.NotNull(moduleIds, nameof(moduleIds));

        var currentIds = _forms.Select(f => f.FormModuleId).ToHashSet();
        var newIds = moduleIds.ToHashSet();

        _forms.RemoveAll(f => !newIds.Contains(f.FormModuleId));

        foreach (var moduleId in newIds)
        {
            if (!currentIds.Contains(moduleId))
            {
                _forms.Add(CollectionForm.Create(Id, moduleId));
            }
        }
    }

    // BLUM-9103: records, for each form that has no version yet, the version this collection will use. A form that
    // already has one keeps it here; moving it onto a newer version is FollowLatestFormVersions' job. A form with no
    // entry in `latestVersions` (a maturity catalogue section, which has no versions) stays unrecorded.
    public void PinFormVersions(IReadOnlyDictionary<FormModuleId, FormModuleVersionId> latestVersions)
    {
        Guard.NotNull(latestVersions, nameof(latestVersions));

        foreach (var form in _forms.Where(f => f.FormModuleVersionId is null))
        {
            if (latestVersions.TryGetValue(form.FormModuleId, out var versionId))
            {
                form.PinVersion(versionId);
            }
        }
    }

    // BLUM-9103 (the PO's answer 1): a published collection uses the latest published version of each form until its
    // reporting window opens, so its recorded versions move whenever a newer one is published while it is Scheduled.
    // From the open date on they are fixed. Draft collections are never moved: publishing one records the latest.
    // Whether a report has already started (an open date moved back into the future) is the caller's check, because
    // it needs the submissions.
    public bool FollowsLatestFormVersions(DateTime utcNow) => GetStatus(utcNow).Equals(CollectionStatus.Scheduled);

    // BLUM-9103: moves each recorded version onto its form's latest version, returning what moved. Used for collections
    // that FollowsLatestFormVersions, and for every collection while form versioning (BLUM_9087) is off: then every
    // report reads the live form, which FormModuleVersionSync keeps equal to the latest version, and switching the flag
    // on must not hand open reports the questions they had before a release. A form with no recorded version is left
    // alone: linking it is PinFormVersions' (or the backfill's) job.
    public IReadOnlyList<FormVersionMove> FollowLatestFormVersions(IReadOnlyDictionary<FormModuleId, FormModuleVersionId> latestVersions)
    {
        Guard.NotNull(latestVersions, nameof(latestVersions));

        var moves = new List<FormVersionMove>();
        foreach (var form in _forms)
        {
            if (form.FormModuleVersionId is { } recorded
                && latestVersions.TryGetValue(form.FormModuleId, out var latest)
                && recorded != latest)
            {
                form.PinVersion(latest);
                moves.Add(new FormVersionMove(form.FormModuleId, recorded, latest));
            }
        }

        return moves;
    }

    public void SetLocalAuthorities(IReadOnlyList<OrganisationId> laIds, DateTime utcNow)
    {
        Guard.NotNull(laIds, nameof(laIds));

        var currentIds = _localAuthorities.Select(la => la.LocalAuthorityId).ToHashSet();
        var newIds = laIds.ToHashSet();

        _localAuthorities.RemoveAll(la => !newIds.Contains(la.LocalAuthorityId));

        foreach (var laId in newIds)
        {
            if (!currentIds.Contains(laId))
            {
                _localAuthorities.Add(CollectionLocalAuthority.Create(CollectionLocalAuthorityId.New(), Id, laId, utcNow));
            }
        }
    }

    public void SetReminders(IReadOnlyList<(ReminderOffsetType Offset, int Days)> reminders, DateTime utcNow)
    {
        Guard.NotNull(reminders, nameof(reminders));

        _reminders.Clear();

        foreach (var (offset, days) in reminders)
        {
            _reminders.Add(CollectionReminder.Create(CollectionReminderId.New(), Id, offset, days, utcNow));
        }
    }

    public bool IsDeleted => DeletedAt is not null;

    public CollectionStatus GetStatus(DateTime utcNow)
    {
        var today = DateOnly.FromDateTime(utcNow);

        if (IsDraft)
        {
            return CollectionStatus.Draft;
        }

        if (IsManuallyClosed || today > SubmissionDeadline)
        {
            return CollectionStatus.Closed;
        }

        if (today < CollectionOpenDate)
        {
            return CollectionStatus.Scheduled;
        }

        return CollectionStatus.Open;
    }

    private void EnsureEditable(DateTime utcNow)
    {
        // CollectionStatus is a ValueObject (reference type) with no operator==, so compare by value via Equals
        // rather than ==, which would be reference equality and never match a freshly-built CollectionStatus.Closed.
        if (GetStatus(utcNow).Equals(CollectionStatus.Closed))
        {
            throw new CollectionNotEditableException(Id, GetStatus(utcNow));
        }
    }

    // Values that ARE present are always validated; missing values are only an error when the collection must be
    // complete (requireComplete == true, i.e. it is not staying a draft). FluentValidation enforces the same
    // completeness rules with user-facing messages before the domain — these throws are a backstop.
    private static int? ValidateWindow(
        bool requireComplete,
        CollectionType? type,
        string? financialYear,
        int? quarter,
        DateOnly? periodStart,
        DateOnly? periodEnd,
        bool? windowSameAsReportingPeriod,
        DateOnly? openDate,
        DateOnly? deadline,
        bool? isSubmittedByAllLas)
    {
        if (requireComplete)
        {
            if (type is null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            Guard.NotNullOrEmptyOrWhiteSpace(financialYear!, nameof(financialYear));

            if (periodStart is null || periodEnd is null)
            {
                throw new CollectionInvalidWindowException("A reporting period is required.");
            }

            if (windowSameAsReportingPeriod is null)
            {
                throw new CollectionInvalidWindowException("Whether the collection window matches the reporting period must be answered.");
            }

            if (openDate is null || deadline is null)
            {
                throw new CollectionInvalidWindowException("A collection open date and submission deadline are required.");
            }

            if (isSubmittedByAllLas is null)
            {
                throw new CollectionInvalidWindowException("Whether the collection is submitted by all Local Authorities must be answered.");
            }
        }

        if (periodStart >= periodEnd)
        {
            throw new CollectionInvalidWindowException("Reporting period start must be before the reporting period end.");
        }

        if (openDate > deadline)
        {
            throw new CollectionInvalidWindowException("Collection open date must be on or before the submission deadline.");
        }

        if (type is not null && !type.IsQuarterly)
        {
            return null;
        }

        if (type is not null && requireComplete && quarter is null)
        {
            throw new CollectionQuarterRequiredException();
        }

        if (quarter is < 1 or > 4)
        {
            throw new CollectionQuarterRequiredException();
        }

        return quarter;
    }

    private static string? Normalise(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}