namespace Application.Collections.Commands;

// Transport shape for a single collection reminder on create/update commands: the offset type
// (ReminderOffsetTypes.AfterOpen / BeforeDeadline) and the number of days.
public record ReminderInput(string OffsetType, int Days);