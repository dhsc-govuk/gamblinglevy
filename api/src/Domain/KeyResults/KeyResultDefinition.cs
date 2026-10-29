using Domain.Categories;
using Domain.Common;
using Domain.Users;

namespace Domain.KeyResults;

// A standard key result an LA can select when creating an activity of a given type (Question Protocol, BLUM-6611).
// Selections are snapshotted onto ActivityExpectedResult rows via the stable Code; the quarterly-report question
// protocol (KeyResultQuestion) keys on the same Code to resolve which questions an activity must answer. System-owned
// reference data seeded at startup — not editable through the forms builder. Tasks that share a protocol block get
// their own duplicate rows at seed time, so resolution is always a single ActivityTaskId join.
public class KeyResultDefinition : IEntity<KeyResultDefinitionId>
{
    public KeyResultDefinitionId Id { get; private set; }
    public ActivityTaskId ActivityTaskId { get; private set; }
    public string Code { get; private set; }
    public string Label { get; private set; }
    public int DisplayOrder { get; private set; }
    public bool IsActive { get; private set; }
    public UserId CreatedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private KeyResultDefinition(
        KeyResultDefinitionId id,
        ActivityTaskId activityTaskId,
        string code,
        string label,
        int displayOrder,
        bool isActive,
        UserId createdBy,
        DateTime createdAt)
    {
        Id = id;
        ActivityTaskId = activityTaskId;
        Code = code;
        Label = label;
        DisplayOrder = displayOrder;
        IsActive = isActive;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
    }

    public static KeyResultDefinition Seed(
        KeyResultDefinitionId id,
        ActivityTaskId activityTaskId,
        string code,
        string label,
        int displayOrder,
        UserId createdBy,
        DateTime utcNow)
    {
        Guard.NotNull(id, nameof(id));
        Guard.NotNull(activityTaskId, nameof(activityTaskId));
        Guard.NotNullOrEmptyOrWhiteSpace(code, nameof(code));
        Guard.NotNullOrEmptyOrWhiteSpace(label, nameof(label));
        Guard.NotNull(createdBy, nameof(createdBy));

        return new KeyResultDefinition(
            id,
            activityTaskId,
            code.Trim(),
            label.Trim(),
            displayOrder,
            isActive: true,
            createdBy,
            utcNow);
    }

    // The seeder's only edit to an existing key result: it puts a task's catalogued key results back in protocol
    // order after it has inserted one among them (BLUM-8749).
    public void SetDisplayOrder(int displayOrder)
    {
        Guard.GreaterThanOrEqual(displayOrder, 0, nameof(displayOrder));

        DisplayOrder = displayOrder;
    }
}