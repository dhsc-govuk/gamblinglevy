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
    string Section,

    // The protocol's free-text character limit and the message shown when it is exceeded (BLUM-7620). Only the
    // "Activity delivery" questions carry one; the results-table questions have no limit.
    int? MaxLength = null,
    string? MaxLengthErrorMessage = null)
{
    // Answer options DHSC has withdrawn from the question's form version (a restore brings back, inactive, a value a
    // later version published). Options holds only what an LA may pick, so everything that offers or accepts an
    // answer reads Options alone; these are kept solely so a stored value still turns into its label (BLUM-9104).
    public IReadOnlyList<ResolvedQuestionOption> WithdrawnOptions { get; init; } = [];
}