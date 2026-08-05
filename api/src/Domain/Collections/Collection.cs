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
        bool saveAsDraft)
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
            DateTime.UtcNow);
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
        bool saveAsDraft)
    {
        EnsureEditable();
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
        UpdatedAt = DateTime.UtcNow;
    }

    public void Publish(UserId actor)
    {
        Guard.NotNull(actor, nameof(actor));

        IsDraft = false;
        UpdatedBy = actor;
        UpdatedAt = DateTime.UtcNow;
    }

    public void RevertToDraft(UserId actor)
    {
        Guard.NotNull(actor, nameof(actor));

        IsDraft = true;
        IsManuallyClosed = false;
        UpdatedBy = actor;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Close(UserId actor)
    {
        Guard.NotNull(actor, nameof(actor));

        IsManuallyClosed = true;
        UpdatedBy = actor;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SoftDelete(UserId actor)
    {
        Guard.NotNull(actor, nameof(actor));
        if (IsDeleted)
        {
            throw new CollectionAlreadyDeletedException(Id);
        }

        DeletedAt = DateTime.UtcNow;
        DeletedBy = actor;
        UpdatedBy = actor;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Restore(UserId actor)
    {
        Guard.NotNull(actor, nameof(actor));
        if (!IsDeleted)
        {
            throw new CollectionNotDeletedException(Id);
        }

        DeletedAt = null;
        DeletedBy = null;
        UpdatedBy = actor;
        UpdatedAt = DateTime.UtcNow;
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

    public void SetLocalAuthorities(IReadOnlyList<OrganisationId> laIds)
    {
        Guard.NotNull(laIds, nameof(laIds));

        var currentIds = _localAuthorities.Select(la => la.LocalAuthorityId).ToHashSet();
        var newIds = laIds.ToHashSet();

        _localAuthorities.RemoveAll(la => !newIds.Contains(la.LocalAuthorityId));

        foreach (var laId in newIds)
        {
            if (!currentIds.Contains(laId))
            {
                _localAuthorities.Add(CollectionLocalAuthority.Create(CollectionLocalAuthorityId.New(), Id, laId));
            }
        }
    }

    public void SetReminders(IReadOnlyList<(ReminderOffsetType Offset, int Days)> reminders)
    {
        Guard.NotNull(reminders, nameof(reminders));

        _reminders.Clear();

        foreach (var (offset, days) in reminders)
        {
            _reminders.Add(CollectionReminder.Create(CollectionReminderId.New(), Id, offset, days));
        }
    }

    public bool IsDeleted => DeletedAt is not null;

    public CollectionStatus GetStatus()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

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

    private void EnsureEditable()
    {
        // CollectionStatus is a ValueObject (reference type) with no operator==, so compare by value via Equals
        // rather than ==, which would be reference equality and never match a freshly-built CollectionStatus.Closed.
        if (GetStatus().Equals(CollectionStatus.Closed))
        {
            throw new CollectionNotEditableException(Id, GetStatus());
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