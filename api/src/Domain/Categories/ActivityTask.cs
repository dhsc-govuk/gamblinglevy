using Domain.Common;
using Domain.Users;

namespace Domain.Categories;

// Sub-area within a Category. Named ActivityTask to avoid collision with System.Threading.Tasks.Task.
// Read-only DHSC-managed reference data (seeded; no management commands in this feature).
public class ActivityTask : IEntity<ActivityTaskId>
{
    public ActivityTaskId Id { get; private set; }
    public CategoryId CategoryId { get; private set; }
    public string TaskName { get; private set; }
    public string? Description { get; private set; }
    public int SequenceOrder { get; private set; }
    public bool IsActive { get; private set; }
    public UserId CreatedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private ActivityTask(
        ActivityTaskId id,
        CategoryId categoryId,
        string taskName,
        string? description,
        int sequenceOrder,
        bool isActive,
        UserId createdBy,
        DateTime createdAt,
        DateTime? updatedAt)
    {
        Id = id;
        CategoryId = categoryId;
        TaskName = taskName;
        Description = description;
        SequenceOrder = sequenceOrder;
        IsActive = isActive;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public static ActivityTask Seed(
        ActivityTaskId id,
        CategoryId categoryId,
        string taskName,
        int sequenceOrder,
        UserId createdBy,
        string? description = null,
        bool isActive = true)
    {
        Guard.NotNull(id, nameof(id));
        Guard.NotNull(categoryId, nameof(categoryId));
        Guard.NotNullOrEmptyOrWhiteSpace(taskName, nameof(taskName));
        Guard.NotNull(createdBy, nameof(createdBy));

        return new ActivityTask(id, categoryId, taskName.Trim(), description, sequenceOrder, isActive, createdBy, DateTime.UtcNow, null);
    }
}