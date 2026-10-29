using Domain.Categories;
using Domain.Common;
using Domain.Users;

namespace Domain.Forms;

// A grouping of activity-record questions. Sections are ordered and rendered as individual steps in the
// activity-creation flow. DHSC admins manage sections through the Form Designer (BLUM-6444). A section may be
// scoped to a single activity Category (CategoryId set) or apply to all categories (CategoryId null). It may
// additionally be scoped to one activity type (ActivityTaskId set) when a form section applies only to that
// type rather than the whole category.
public class FormSection : IEntity<FormSectionId>
{
    public FormSectionId Id { get; private set; }
    public CategoryId? CategoryId { get; private set; }
    public ActivityTaskId? ActivityTaskId { get; private set; }
    public string Title { get; private set; }
    public string? Description { get; private set; }
    public int DisplayOrder { get; private set; }
    public bool IsActive { get; private set; }
    public UserId CreatedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private FormSection(
        FormSectionId id,
        CategoryId? categoryId,
        ActivityTaskId? activityTaskId,
        string title,
        string? description,
        int displayOrder,
        bool isActive,
        UserId createdBy,
        DateTime createdAt,
        DateTime? updatedAt)
    {
        Id = id;
        CategoryId = categoryId;
        ActivityTaskId = activityTaskId;
        Title = title;
        Description = description;
        DisplayOrder = displayOrder;
        IsActive = isActive;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public static FormSection Create(
        FormSectionId id,
        string title,
        int displayOrder,
        UserId createdBy,
        DateTime utcNow,
        string? description = null,
        CategoryId? categoryId = null,
        ActivityTaskId? activityTaskId = null)
    {
        Guard.NotNull(id, nameof(id));
        Guard.NotNullOrEmptyOrWhiteSpace(title, nameof(title));
        Guard.NotNull(createdBy, nameof(createdBy));

        return new FormSection(
            id,
            categoryId,
            activityTaskId,
            title.Trim(),
            Normalise(description),
            displayOrder,
            isActive: true,
            createdBy,
            utcNow,
            null);
    }

    // Applicability of a (possibly type-scoped) section to an activity's chosen type. Unscoped sections apply
    // to every type; scoped sections only when the type matches. Mirrored client-side in the activity wizard.
    public bool AppliesToTask(ActivityTaskId? taskId) =>
        ActivityTaskId is null || (taskId is not null && ActivityTaskId.Value == taskId.Value);

    public void UpdateDetails(string title, string? description, DateTime utcNow)
    {
        Guard.NotNullOrEmptyOrWhiteSpace(title, nameof(title));

        Title = title.Trim();
        Description = Normalise(description);
        UpdatedAt = utcNow;
    }

    public void SetCategory(CategoryId? categoryId, DateTime utcNow)
    {
        CategoryId = categoryId;
        UpdatedAt = utcNow;
    }

    public void SetDisplayOrder(int displayOrder, DateTime utcNow)
    {
        DisplayOrder = displayOrder;
        UpdatedAt = utcNow;
    }

    public void Activate(DateTime utcNow)
    {
        IsActive = true;
        UpdatedAt = utcNow;
    }

    public void Deactivate(DateTime utcNow)
    {
        IsActive = false;
        UpdatedAt = utcNow;
    }

    private static string? Normalise(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}