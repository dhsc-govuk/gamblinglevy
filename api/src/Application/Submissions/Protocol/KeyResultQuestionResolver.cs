using Domain.Activities;
using Domain.DataCollectionForms;
using Domain.KeyResults;

namespace Application.Submissions.Protocol;

// Resolves the question set an LA must answer for one activity in the "Enter activity key results" section:
// the protocol questions matching the activity's type, its selected standard key results and its current status
// phrasing variant; a generic progress question per custom ("Other") key result; and the module's unconditional
// "Activity delivery" questions. Pure static (the SaveActivityReport handler and the controller service feed it
// pre-fetched data) so the variant/conditional logic is unit-testable without a database.
public static class KeyResultQuestionResolver
{
    public const string ResultsSection = "results";
    public const string DeliverySection = "delivery";

    public const string WhoWithPrefillSource = "who_with";
    public const string WhoWithOtherPrefillSource = "who_with_other";

    private static readonly IReadOnlyList<ResolvedQuestionOption> YesNoOptions =
    [
        new("yes", "Yes"),
        new("no", "No"),
    ];

    public static IReadOnlyList<ResolvedQuestion> Resolve(
        Activity activity,
        IReadOnlyList<ActivityExpectedResult> expectedResults,
        IReadOnlyList<KeyResultQuestion> questionBank,
        IReadOnlyList<DcFormQuestion> deliveryQuestions)
    {
        var resolved = new List<ResolvedQuestion>();
        var order = 0;

        var status = activity.Status;
        if (status is not null)
        {
            // Standard key results unlock their protocol questions, in the order the results were selected on the
            // activity; only the phrasing variant matching the activity's current status is asked.
            foreach (var expectedResult in expectedResults.Where(r => !r.IsCustom).OrderBy(r => r.DisplayOrder))
            {
                var questions = questionBank
                    .Where(q => q.KeyResultCode == expectedResult.KeyResultCode && q.StatusScope.Matches(status))
                    .OrderBy(q => q.DisplayOrder);

                foreach (var question in questions)
                {
                    resolved.Add(new ResolvedQuestion(
                        question.Id.Value,
                        question.Code,
                        question.Label,
                        question.Hint,
                        question.FieldType.ToRendererQuestionType(),
                        question.IsRequiredOnComplete,
                        order++,
                        question.ConditionalQuestionCode,
                        question.ConditionalValue,
                        BuildOptions(question),
                        question.MinValue,
                        question.MaxValue,
                        question.FieldType.Equals(ProtocolFieldType.Percentage),
                        question.PrefillSource,
                        question.ErrorMessage,
                        ResultsSection));
                }
            }
        }

        // Custom ("Other" / legacy free-text) key results each get a generic progress question. The code embeds the
        // expected-result row id, which is stable because UpdateActivity diff/merges rows via ExpectedResultSync
        // instead of recreating them (BLUM-6805), so answers persist across activity edits.
        foreach (var custom in expectedResults.Where(r => r.IsCustom).OrderBy(r => r.DisplayOrder))
        {
            resolved.Add(new ResolvedQuestion(
                custom.Id.Value,
                $"CUSTOM_{custom.Id.Value:N}",
                $"Provide progress details for: {custom.Text}",
                null,
                Domain.Forms.QuestionTypes.Textarea,
                IsRequired: true,
                order++,
                null,
                null,
                [],
                null,
                null,
                IsPercentage: false,
                null,
                "Enter the key result",
                ResultsSection));
        }

        foreach (var question in deliveryQuestions.Where(q => q.IsActive).OrderBy(q => q.DisplayOrder))
        {
            resolved.Add(new ResolvedQuestion(
                question.Id.Value,
                question.Code,
                question.Label,
                question.Hint,
                question.QuestionType.ToString(),
                question.IsRequired,
                order++,
                null,
                null,
                question.Options.OrderBy(o => o.DisplayOrder).Select(o => new ResolvedQuestionOption(o.Value, o.Label)).ToList(),
                null,
                null,
                IsPercentage: false,
                null,
                null,
                DeliverySection));
        }

        return resolved;
    }

    // Initial values for first-time entry, mapped from the activity record's saved answers (e.g. "Who is
    // contributing evidence?" pre-selects the activity's "Who will you work with?" choices). Saved report answers
    // take precedence at the merge site.
    public static IReadOnlyDictionary<string, string?> BuildPrefill(
        IReadOnlyList<ResolvedQuestion> questions,
        IReadOnlyList<ActivityAnswer> activityAnswers)
    {
        var prefill = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var question in questions)
        {
            var sourceValue = question.PrefillSource switch
            {
                WhoWithPrefillSource => FindAnswerBySuffix(activityAnswers, "_WHO_WITH"),
                WhoWithOtherPrefillSource => FindAnswerBySuffix(activityAnswers, "_WHO_WITH_OTHER"),
                _ => null,
            };

            if (!string.IsNullOrWhiteSpace(sourceValue))
            {
                prefill[question.Code] = sourceValue;
            }
        }

        return prefill;
    }

    private static IReadOnlyList<ResolvedQuestionOption> BuildOptions(KeyResultQuestion question)
    {
        if (question.FieldType.IsBoolean)
        {
            return YesNoOptions;
        }

        return question.Options?.Select(o => new ResolvedQuestionOption(o.Value, o.Label)).ToList() ?? [];
    }

    // Activity-record answer codes are category-prefixed (NA_WHO_WITH, EDU_WHO_WITH, ...), so prefill sources match
    // by suffix. The exact-suffix check keeps _WHO_WITH from also matching _WHO_WITH_OTHER.
    private static string? FindAnswerBySuffix(IReadOnlyList<ActivityAnswer> answers, string suffix) =>
        answers.FirstOrDefault(a =>
            a.QuestionCode.EndsWith(suffix, StringComparison.Ordinal)
            && (suffix != "_WHO_WITH" || !a.QuestionCode.EndsWith("_WHO_WITH_OTHER", StringComparison.Ordinal)))?.Value;
}