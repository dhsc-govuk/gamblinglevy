using Application.Common.Interfaces.Queries;
using Domain.Activities;
using Domain.DataCollectionForms;
using Domain.KeyResults;
using Domain.Submissions;

namespace Application.Submissions.Protocol;

// Resolves the question set an LA must answer for one activity in the "Enter activity results" section:
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

    // isJointPartnership swaps the delivery questions' wording for the partnership copy (BLUM-8090) — see
    // JointPartnershipDeliveryCopy. It is a flag rather than a category lookup because this class stays pure: the
    // Activity carries only a CategoryId, and every caller already has the category (or its name) to hand.
    public static IReadOnlyList<ResolvedQuestion> Resolve(
        Activity activity,
        IReadOnlyList<ActivityExpectedResult> expectedResults,
        IReadOnlyList<KeyResultQuestion> questionBank,
        IReadOnlyList<DcFormQuestion> deliveryQuestions,
        bool isJointPartnership = false)
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
                    resolved.Add(ToResolved(question, order++));
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
                $"What progress have you made towards '{custom.Text}'?",
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
                $"Enter an update for {custom.Text}",
                ResultsSection));
        }

        foreach (var question in deliveryQuestions.Where(q => q.IsActive).OrderBy(q => q.DisplayOrder))
        {
            resolved.Add(ToDelivery(question, order++, isJointPartnership));
        }

        return resolved;
    }

    // The questions a finalised report actually answered for one activity, each paired with its saved value — the
    // activity-record export's "what did we report" rows (BLUM-7648). Every saved answer is kept: the save and
    // complete commands only ever persist visible, non-blank answers, and a submitted report cannot be edited, so the
    // stored set already is what was submitted.
    //
    // The report may predate a change to the activity (a later status, a removed measure), so a code the activity's
    // CURRENT resolution no longer asks still has to be labelled rather than dropped:
    //   1. codes the current resolution asks, in its order and wording;
    //   2. then the protocol question with that code in any status phrasing (the phrasings share one code);
    //   3. then a delivery question with that code, retired ones included;
    //   4. then a free-text row labelled with the code itself — a custom measure since removed from the activity
    //      leaves a CUSTOM_{id} code with nothing left to name it.
    public static IReadOnlyList<(ResolvedQuestion Question, string Value)> ResolveAnswered(
        Activity activity,
        IReadOnlyList<ActivityExpectedResult> expectedResults,
        IReadOnlyList<KeyResultQuestion> questionBank,
        IReadOnlyList<DcFormQuestion> deliveryQuestions,
        IReadOnlyDictionary<string, string> answers,
        bool isJointPartnership = false)
    {
        var answered = new List<(ResolvedQuestion Question, string Value)>();
        var labelled = new HashSet<string>(StringComparer.Ordinal);

        void Add(ResolvedQuestion question)
        {
            if (answers.TryGetValue(question.Code, out var value)
                && !string.IsNullOrWhiteSpace(value)
                && labelled.Add(question.Code))
            {
                answered.Add((question, value));
            }
        }

        foreach (var question in Resolve(activity, expectedResults, questionBank, deliveryQuestions, isJointPartnership))
        {
            Add(question);
        }

        foreach (var question in questionBank.OrderBy(q => q.DisplayOrder))
        {
            Add(ToResolved(question, answered.Count));
        }

        foreach (var question in deliveryQuestions.OrderBy(q => q.DisplayOrder))
        {
            Add(ToDelivery(question, answered.Count, isJointPartnership));
        }

        foreach (var code in answers.Keys.Where(code => !labelled.Contains(code)).OrderBy(code => code, StringComparer.Ordinal).ToList())
        {
            Add(new ResolvedQuestion(
                Guid.Empty,
                code,
                code,
                null,
                Domain.Forms.QuestionTypes.Textarea,
                IsRequired: false,
                answered.Count,
                null,
                null,
                [],
                null,
                null,
                IsPercentage: false,
                null,
                null,
                ResultsSection));
        }

        return answered;
    }

    public static ResolvedQuestion ToResolved(KeyResultQuestion question, int order) => new(
        question.Id.Value,
        question.Code,
        question.Label,
        question.Hint,
        question.FieldType.ToRendererQuestionType(),
        question.IsRequiredOnComplete,
        order,
        question.ConditionalQuestionCode,
        question.ConditionalValue,
        BuildOptions(question),
        question.MinValue,
        question.MaxValue,
        question.FieldType.Equals(ProtocolFieldType.Percentage),
        question.PrefillSource,
        question.ErrorMessage,
        ResultsSection);

    // A withdrawn option (inactive in the collection's version) is never offered or accepted, as on strategic
    // feedback, but rides along in WithdrawnOptions so an answer given with it keeps its label.
    public static ResolvedQuestion ToResolved(DcFormQuestion question, int order) => new(
        question.Id.Value,
        question.Code,
        question.Label,
        question.Hint,
        question.QuestionType.ToString(),
        question.IsRequired,
        order,
        null,
        null,
        DeliveryOptions(question, active: true),
        null,
        null,
        IsPercentage: false,
        null,
        question.ErrorMessage,
        DeliverySection,
        question.MaxLength,
        question.MaxLengthErrorMessage)
    {
        WithdrawnOptions = DeliveryOptions(question, active: false),
    };

    private static ResolvedQuestion ToDelivery(DcFormQuestion question, int order, bool isJointPartnership)
    {
        var resolved = ToResolved(question, order);
        return isJointPartnership ? JointPartnershipDeliveryCopy.Apply(resolved) : resolved;
    }

    // The values an activity's report questions start from: the activity-record prefill, with the answers already
    // saved in this report on top. The web form (QuarterlyReportControllerService) and the offline template
    // (BLUM-8310) both show exactly this, so what an LA sees on screen and in the spreadsheet cannot differ.
    public static IReadOnlyDictionary<string, string?> InitialValues(
        IReadOnlyList<ResolvedQuestion> questions,
        IReadOnlyList<ActivityAnswer> activityAnswers,
        IReadOnlyList<SubmissionActivityAnswer> savedAnswers)
    {
        var values = new Dictionary<string, string?>(BuildPrefill(questions, activityAnswers), StringComparer.Ordinal);
        foreach (var answer in savedAnswers)
        {
            values[answer.QuestionCode] = answer.Value;
        }

        return values;
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

    // Only a FINISHED activity carries its previously submitted answers into a later report (BLUM-7671): its
    // results are final, so re-keying them every quarter is pure toil. Active and Closed activities deliberately
    // do not — their answers are expected to move, and seeding last quarter's would invite an unchanged
    // resubmission. Callers check this BEFORE querying so nothing but a finished activity pays for the read.
    public static bool CarriesForwardPreviousAnswers(ActivityStatus? status) =>
        status is not null && status.Equals(ActivityStatus.Finished);

    // Values from the organisation's most recent EARLIER submitted report for this activity. Rows arrive
    // most-recent-period-first, so the first row names the source report and ONLY that report's rows are carried.
    // Filling gaps per code from older reports would resurrect deleted data: a blank is never persisted (every
    // answer validator drops empty values before saving), so a code the LA deliberately cleared in Q2 is
    // indistinguishable from one it never answered, and a per-code fallback would hand back the Q1 value in Q3.
    // The query joins through the answers table, so the source is the most recent earlier report that actually
    // reported on this activity — still correct if the activity was soft-deleted for a quarter and restored.
    // Restricted to codes that still resolve to a question here: an activity that was Active last quarter saved
    // answers under active-only codes (SCR_TOOL, LSG_MEMBERS, ...) which the closed/finished phrasing never asks,
    // and handing those back would put values on the wire for questions that do not exist. Blank values are
    // skipped for the same reason nothing else stores them.
    public static IReadOnlyDictionary<string, string?> BuildCarryForward(
        IReadOnlyList<ResolvedQuestion> questions,
        IReadOnlyList<PriorActivityAnswerRow> priorAnswers)
    {
        var carried = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (priorAnswers.Count == 0)
        {
            return carried;
        }

        var resolvedCodes = questions.Select(q => q.Code).ToHashSet(StringComparer.Ordinal);
        var sourceSubmissionId = priorAnswers[0].SubmissionId;

        foreach (var answer in priorAnswers)
        {
            if (answer.SubmissionId != sourceSubmissionId
                || string.IsNullOrWhiteSpace(answer.Value)
                || !resolvedCodes.Contains(answer.QuestionCode)
                || carried.ContainsKey(answer.QuestionCode))
            {
                continue;
            }

            carried[answer.QuestionCode] = answer.Value;
        }

        return carried;
    }

    private static IReadOnlyList<ResolvedQuestionOption> BuildOptions(KeyResultQuestion question)
    {
        if (question.FieldType.IsBoolean)
        {
            return YesNoOptions;
        }

        return question.Options?.Select(o => new ResolvedQuestionOption(o.Value, o.Label)).ToList() ?? [];
    }

    private static IReadOnlyList<ResolvedQuestionOption> DeliveryOptions(DcFormQuestion question, bool active) =>
        question.Options
            .Where(o => o.IsActive == active)
            .OrderBy(o => o.DisplayOrder)
            .Select(o => new ResolvedQuestionOption(o.Value, o.Label))
            .ToList();

    // Activity-record answer codes are category-prefixed (NA_WHO_WITH, EDU_WHO_WITH, ...), so prefill sources match
    // by suffix. The exact-suffix check keeps _WHO_WITH from also matching _WHO_WITH_OTHER.
    private static string? FindAnswerBySuffix(IReadOnlyList<ActivityAnswer> answers, string suffix) =>
        answers.FirstOrDefault(a =>
            a.QuestionCode.EndsWith(suffix, StringComparison.Ordinal)
            && (suffix != "_WHO_WITH" || !a.QuestionCode.EndsWith("_WHO_WITH_OTHER", StringComparison.Ordinal)))?.Value;
}