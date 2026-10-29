using Domain.Categories;
using Domain.Common;
using Domain.Organisations;
using Domain.Users;

namespace Domain.Activities;

// An activity record. Setup runs as a multi-step wizard; a record saved part-way ("Save and come back later")
// is a Draft (IsComplete == false) and may not have a Status yet. Once setup is complete the user has chosen
// a lifecycle Status (Planned/Active/Closed/Finished). Category and type are locked once setup is complete.
// Delete is a soft-delete (DeletedAt) so records can be restored from the "Deleted activities" list.
public class Activity : IEntity<ActivityId>
{
    public ActivityId Id { get; private set; }
    public OrganisationId OrganisationId { get; private set; }
    public string? Title { get; private set; }
    public string? Description { get; private set; }
    public CategoryId? CategoryId { get; private set; }
    public ActivityTaskId? TaskId { get; private set; }
    public string? CustomTaskText { get; private set; }
    public ActivityStatus? Status { get; private set; }
    public bool IsComplete { get; private set; }
    public int? StartMonth { get; private set; }
    public int? StartYear { get; private set; }

    // Why a Closed record was closed (BLUM-7673). Null for every other status, like the start date is for every
    // status but Active; records closed before the field existed have none.
    public string? ClosureReason { get; private set; }
    public bool IsFlaggedForReview { get; private set; }
    public DateTime? DeletedAt { get; private set; }
    public UserId? DeletedBy { get; private set; }
    public UserId CreatedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private Activity(
        ActivityId id,
        OrganisationId organisationId,
        string? title,
        string? description,
        CategoryId? categoryId,
        ActivityTaskId? taskId,
        string? customTaskText,
        ActivityStatus? status,
        bool isComplete,
        int? startMonth,
        int? startYear,
        string? closureReason,
        bool isFlaggedForReview,
        UserId createdBy,
        DateTime createdAt,
        DateTime? updatedAt)
    {
        Id = id;
        OrganisationId = organisationId;
        Title = title;
        Description = description;
        CategoryId = categoryId;
        TaskId = taskId;
        CustomTaskText = customTaskText;
        Status = status;
        IsComplete = isComplete;
        StartMonth = startMonth;
        StartYear = startYear;
        ClosureReason = closureReason;
        IsFlaggedForReview = isFlaggedForReview;
        DeletedAt = null;
        DeletedBy = null;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    // Create an incomplete record saved as a Draft ("Save and come back later"). Only the owning org and actor
    // are required; every other field can be filled in later before setup is completed.
    public static Activity NewDraft(
        ActivityId id,
        OrganisationId organisationId,
        UserId createdBy,
        DateTime utcNow,
        string? title = null,
        CategoryId? categoryId = null,
        ActivityTaskId? taskId = null,
        string? customTaskText = null,
        ActivityStatus? status = null,
        int? startMonth = null,
        int? startYear = null,
        string? closureReason = null)
    {
        Guard.NotNull(id, nameof(id));
        Guard.NotNull(organisationId, nameof(organisationId));
        Guard.NotNull(createdBy, nameof(createdBy));

        return new Activity(
            id,
            organisationId,
            Normalise(title),
            null,
            categoryId,
            taskId,
            Normalise(customTaskText),
            status,
            isComplete: false,
            startMonth,
            startYear,
            ClosureReasonFor(status, closureReason),
            isFlaggedForReview: HasCustomText(customTaskText),
            createdBy,
            utcNow,
            null);
    }

    // Create a fully-formed (completed-setup) record. Category, type and status are mandatory once setup is done.
    public static Activity NewComplete(
        ActivityId id,
        OrganisationId organisationId,
        UserId createdBy,
        CategoryId categoryId,
        ActivityTaskId taskId,
        ActivityStatus status,
        DateTime utcNow,
        string? title = null,
        string? customTaskText = null,
        int? startMonth = null,
        int? startYear = null,
        string? closureReason = null)
    {
        Guard.NotNull(id, nameof(id));
        Guard.NotNull(organisationId, nameof(organisationId));
        Guard.NotNull(createdBy, nameof(createdBy));
        Guard.NotNull(categoryId, nameof(categoryId));
        Guard.NotNull(taskId, nameof(taskId));
        Guard.NotNull(status, nameof(status));

        return new Activity(
            id,
            organisationId,
            Normalise(title),
            null,
            categoryId,
            taskId,
            Normalise(customTaskText),
            status,
            isComplete: true,
            startMonth,
            startYear,
            ClosureReasonFor(status, closureReason),
            isFlaggedForReview: HasCustomText(customTaskText),
            createdBy,
            utcNow,
            null);
    }

    // Update the activity name / free-text detail. Blocked once the record is read-only (Closed/Finished).
    public void UpdateDetails(string? title, string? description, DateTime utcNow)
    {
        EnsureEditable();

        Title = Normalise(title);
        Description = Normalise(description);
        UpdatedAt = utcNow;
    }

    // Category and type can only be changed while the record is still a Draft (BLUM redesign).
    public void ChangeCategoryAndType(CategoryId? categoryId, ActivityTaskId? taskId, string? customTaskText, DateTime utcNow)
    {
        if (IsComplete)
        {
            throw new ActivityCategoryLockedException(Id);
        }

        CategoryId = categoryId;
        TaskId = taskId;
        CustomTaskText = Normalise(customTaskText);
        IsFlaggedForReview = HasCustomText(customTaskText);
        UpdatedAt = utcNow;
    }

    // Set / change the lifecycle status. A start month/year is only meaningful when the status is Active, and a
    // closure reason only when it is Closed.
    public void SetStatus(ActivityStatus status, int? startMonth, int? startYear, DateTime utcNow, string? closureReason = null)
    {
        EnsureEditable();
        Guard.NotNull(status, nameof(status));

        Status = status;
        StartMonth = status.IsActive ? startMonth : null;
        StartYear = status.IsActive ? startYear : null;
        ClosureReason = ClosureReasonFor(status, closureReason);
        UpdatedAt = utcNow;
    }

    // Draft -> completed setup. The mandatory-for-completion field set is enforced here.
    public void CompleteSetup(DateTime utcNow)
    {
        Guard.IsTrue(!IsComplete, "Setup is already complete.");
        Guard.NotNull(CategoryId!, nameof(CategoryId));
        Guard.NotNull(TaskId!, nameof(TaskId));
        Guard.NotNull(Status!, nameof(Status));

        IsComplete = true;
        UpdatedAt = utcNow;
    }

    public void SoftDelete(UserId actor, DateTime utcNow)
    {
        Guard.NotNull(actor, nameof(actor));
        if (IsDeleted)
        {
            throw new ActivityAlreadyDeletedException(Id);
        }

        DeletedAt = utcNow;
        DeletedBy = actor;
        UpdatedAt = utcNow;
    }

    public void Restore(UserId actor, DateTime utcNow)
    {
        Guard.NotNull(actor, nameof(actor));
        if (!IsDeleted)
        {
            throw new ActivityNotDeletedException(Id);
        }

        DeletedAt = null;
        DeletedBy = null;
        UpdatedAt = utcNow;
    }

    public bool IsDeleted => DeletedAt is not null;

    private void EnsureEditable()
    {
        if (IsComplete && Status is not null && Status.IsReadOnly)
        {
            throw new ActivityClosedException(Id, Status);
        }
    }

    private static string? Normalise(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? ClosureReasonFor(ActivityStatus? status, string? closureReason) => status?.IsClosed == true ? Normalise(closureReason) : null;

    private static bool HasCustomText(string? customTaskText) => !string.IsNullOrWhiteSpace(customTaskText);
}