using Domain.Common;
using Domain.Users;

namespace Domain.Activities;

// A key result the user will use to measure an activity (BLUM redesign, standardised in BLUM-6611). A row is either
// a STANDARD selection — KeyResultCode references a KeyResultDefinition for the activity's type and Text snapshots
// its label — or a CUSTOM free-text entry (KeyResultCode == null, the "Other" option / legacy rows). The quarterly
// report resolves protocol questions from standard codes and a generic progress question from custom rows.
// Modelled as a separate aggregate with an id-only ActivityId reference (mirroring ActivityAnswer) so it persists
// through its own repository without tracking conflicts.
// The row id is embedded in submission answer codes (CUSTOM_{id:N}), so ids MUST stay stable across activity
// edits — UpdateActivity diff/merges rows via Amend instead of recreating them (BLUM-6805).
public class ActivityExpectedResult : IEntity<ActivityExpectedResultId>
{
    public ActivityExpectedResultId Id { get; private set; }
    public ActivityId ActivityId { get; private set; }
    public string? KeyResultCode { get; private set; }
    public string Text { get; private set; }
    public int DisplayOrder { get; private set; }
    public UserId CreatedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private ActivityExpectedResult(
        ActivityExpectedResultId id,
        ActivityId activityId,
        string? keyResultCode,
        string text,
        int displayOrder,
        UserId createdBy,
        DateTime createdAt)
    {
        Id = id;
        ActivityId = activityId;
        KeyResultCode = keyResultCode;
        Text = text;
        DisplayOrder = displayOrder;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
    }

    public bool IsCustom => KeyResultCode is null;

    // No UpdatedAt bump: the entity has no such column — activity edits are audited via before/after snapshots.
    public void Amend(string text, int displayOrder)
    {
        Guard.NotNullOrEmptyOrWhiteSpace(text, nameof(text));

        Text = text.Trim();
        DisplayOrder = displayOrder;
    }

    public static ActivityExpectedResult Record(
        ActivityExpectedResultId id,
        ActivityId activityId,
        string text,
        int displayOrder,
        UserId createdBy,
        string? keyResultCode = null)
    {
        Guard.NotNull(id, nameof(id));
        Guard.NotNull(activityId, nameof(activityId));
        Guard.NotNullOrEmptyOrWhiteSpace(text, nameof(text));
        Guard.NotNull(createdBy, nameof(createdBy));

        return new ActivityExpectedResult(
            id,
            activityId,
            string.IsNullOrWhiteSpace(keyResultCode) ? null : keyResultCode.Trim(),
            text.Trim(),
            displayOrder,
            createdBy,
            DateTime.UtcNow);
    }
}