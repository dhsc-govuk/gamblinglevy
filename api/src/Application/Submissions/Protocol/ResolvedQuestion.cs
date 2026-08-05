namespace Application.Submissions.Protocol;

public record ResolvedQuestionOption(string Value, string Label);

// One question of an activity's resolved quarterly-report form. The shape mirrors the activity-record
// FormQuestionDto contract (code / label / questionType / options / conditionalQuestionCode+Value) so the frontend
// GovUKDynamicQuestionRenderer consumes it unchanged. QuestionType uses the renderer vocabulary
// (Text/Textarea/Number/Radio/Checkbox/Select); booleans arrive as Radio with yes/no options. Section groups the
// question onto the "Activity results" or "Activity delivery" page.
public record ResolvedQuestion(
    Guid Id,
    string Code,
    string Label,
    string? Hint,
    string QuestionType,
    bool IsRequired,
    int DisplayOrder,
    string? ConditionalQuestionCode,
    string? ConditionalValue,
    IReadOnlyList<ResolvedQuestionOption> Options,
    decimal? MinValue,
    decimal? MaxValue,
    bool IsPercentage,
    string? PrefillSource,
    string? ErrorMessage,
    string Section);