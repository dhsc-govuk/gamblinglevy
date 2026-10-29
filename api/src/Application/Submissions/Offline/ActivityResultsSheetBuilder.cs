using Application.Common.Csv;
using Application.Submissions.Protocol;
using Domain.Activities;
using Domain.KeyResults;

namespace Application.Submissions.Offline;

// One reportable activity as the "Enter activity results" sheet needs it: the questions KeyResultQuestionResolver
// resolved for it (exactly the web form's set), its custom key results in the order it asks them, and the values
// the web form would start from.
public sealed record OfflineActivityInput(
    Activity Activity,
    string Title,
    string? TaskName,
    bool IsJointPartnership,
    IReadOnlyList<ResolvedQuestion> Questions,
    IReadOnlyList<ActivityExpectedResult> CustomResults,
    IReadOnlyDictionary<string, string?> InitialValues);

// "Enter activity results" as a spreadsheet: one row per reportable activity and one column per question any of
// them is asked. Activities ask different questions — by type, by the key results chosen and by status — so a cell
// whose question a row is not asked says N/A.
//
// Column keys (row 3) are rebuilt from the database at upload time, so they must be deterministic:
//   {Code}@{scope}              a protocol question. Its Active and Closed/Finished wordings share a Code (answers
//                               survive a status change) but are separate columns, so a filled cell always shows
//                               the wording the web form shows that activity.
//   {Code} / {Code}@joint_partnership
//                               a delivery question; joint partnerships have their own wording.
//   own_kr_{i}, own_kr_{i}_name the i-th custom key result of the row (its CUSTOM_{id} question), and its text.
public static class ActivityResultsSheetBuilder
{
    public const string ReferenceKey = "ref";
    public const string ActivityKey = "activity";
    public const string TypeKey = "type";
    public const string StatusKey = "status";
    public const string ErrorsKey = "errors";
    public const string JointPartnershipVariant = "joint_partnership";

    private const string CustomCodePrefix = "CUSTOM_";

    // transitive is BLUM_9087: on, a starting value whose question's parent is hidden is left out too.
    public static OfflineSheet Build(
        Guid collectionId,
        Guid organisationId,
        IReadOnlyList<OfflineActivityInput> activities,
        IReadOnlyDictionary<Guid, KeyResultQuestion> protocolQuestionsById,
        bool transitive)
    {
        var protocolColumns = new Dictionary<string, ProtocolColumn>(StringComparer.Ordinal);
        var deliveryKeys = new List<string>();
        var deliveryPrototypes = new Dictionary<string, ResolvedQuestion>(StringComparer.Ordinal);
        var customPrototypes = new Dictionary<int, ResolvedQuestion>();
        var rows = new List<(OfflineActivityInput Input, List<OfflineBinding> Bindings, Dictionary<string, string> ReadOnly)>();

        foreach (var input in activities)
        {
            var bindings = new List<OfflineBinding>();
            var readOnly = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [ActivityKey] = input.Title,
                [TypeKey] = input.TaskName ?? input.Activity.CustomTaskText ?? string.Empty,
                [StatusKey] = OfflineSheetText.StatusLabel(input.Activity.Status),
            };

            var customSlots = input.CustomResults
                .Select((result, index) => (Code: $"{CustomCodePrefix}{result.Id.Value:N}", Slot: index + 1, result.Text))
                .ToDictionary(slot => slot.Code, StringComparer.Ordinal);

            foreach (var question in input.Questions)
            {
                string key;
                if (question.Section == KeyResultQuestionResolver.DeliverySection)
                {
                    key = input.IsJointPartnership ? $"{question.Code}@{JointPartnershipVariant}" : question.Code;
                    if (!deliveryPrototypes.ContainsKey(key))
                    {
                        deliveryPrototypes[key] = question;
                        deliveryKeys.Add(key);
                    }
                }
                else if (customSlots.TryGetValue(question.Code, out var slot))
                {
                    key = CustomKey(slot.Slot);
                    readOnly[CustomNameKey(slot.Slot)] = slot.Text;
                    customPrototypes.TryAdd(slot.Slot, question);
                }
                else if (protocolQuestionsById.TryGetValue(question.Id, out var protocol))
                {
                    var scope = protocol.StatusScope.ToString();
                    key = $"{question.Code}@{scope}";
                    protocolColumns.TryAdd(key, new ProtocolColumn(key, question, input.TaskName, scope, protocol.DisplayOrder));
                }
                else
                {
                    throw new InvalidOperationException($"Question {question.Code} of activity {input.Activity.Id.Value} has no offline column.");
                }

                if (bindings.Any(binding => binding.ColumnKey == key))
                {
                    throw new InvalidOperationException($"Activity {input.Activity.Id.Value} asks two questions in column {key}.");
                }

                bindings.Add(new OfflineBinding(key, question));
            }

            rows.Add((input, bindings, readOnly));
        }

        var specs = new List<OfflineColumnSpec>
        {
            new(ReferenceKey, OfflineColumnKind.Reference, "Reference (do not change)", Guidance: Preamble),
            new(ActivityKey, OfflineColumnKind.ReadOnly, "Activity (do not change)", Guidance: OfflineSheetText.ReadOnlyGuidance),
            new(TypeKey, OfflineColumnKind.ReadOnly, "Activity type (do not change)", Guidance: OfflineSheetText.ReadOnlyGuidance),
            new(StatusKey, OfflineColumnKind.ReadOnly, "Status (do not change)", Guidance: OfflineSheetText.ReadOnlyGuidance),
            new(ErrorsKey, OfflineColumnKind.Errors, OfflineSheetText.ErrorsHeading, Guidance: OfflineSheetText.ErrorsGuidance),
        };

        // Task by task; within a task the Active wordings, then the Closed/Finished ones, each in protocol order. A row
        // therefore fills one unbroken run of columns, and a Finished-only follow-up sits next to its parent.
        foreach (var column in protocolColumns.Values
            .OrderBy(column => column.TaskName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(column => column.Scope == QuestionStatusScopes.Active ? 0 : 1)
            .ThenBy(column => column.DisplayOrder)
            .ThenBy(column => column.Key, StringComparer.Ordinal))
        {
            specs.Add(new OfflineColumnSpec(column.Key, OfflineColumnKind.Question, column.Prototype.Label, column.Prototype, ScopeNote(column)));
        }

        foreach (var slot in customPrototypes.Keys.Order())
        {
            var nameIndex = specs.Count;
            specs.Add(new OfflineColumnSpec(
                CustomNameKey(slot),
                OfflineColumnKind.ReadOnly,
                $"Your own key result {slot} (do not change)",
                Guidance: "Do not change. The key result your answer in the next column is about. N/A means the activity has no key result of its own here."));
            specs.Add(new OfflineColumnSpec(
                CustomKey(slot),
                OfflineColumnKind.Question,
                $"What progress have you made towards your own key result {slot}?",
                customPrototypes[slot] with { Hint = null },
                $"Describe your progress towards the key result in column {SpreadsheetColumn.Letter(nameIndex)}."));
        }

        var hasJointPartnership = deliveryKeys.Any(IsJointPartnershipKey);
        foreach (var key in deliveryKeys.Where(key => !IsJointPartnershipKey(key)).Concat(deliveryKeys.Where(IsJointPartnershipKey)))
        {
            var note = !hasJointPartnership
                ? null
                : IsJointPartnershipKey(key) ? "For joint partnership activities." : "For every activity except joint partnerships.";
            specs.Add(new OfflineColumnSpec(key, OfflineColumnKind.Question, deliveryPrototypes[key].Label, deliveryPrototypes[key], note));
        }

        var sheetRows = rows
            .Select(row => new OfflineRow(
                row.Input.Activity.Id.Value.ToString(),
                row.Input.Title,
                row.ReadOnly,
                row.Bindings,
                VisibleInitialValues(row.Input, transitive)))
            .ToList();

        var fingerprint = OfflineIdentity.ComputeFingerprint(rows.Select(row =>
            $"{row.Input.Activity.Id.Value:N}:{string.Join(',', row.Bindings.Select(binding => binding.ColumnKey).Order(StringComparer.Ordinal))}"
            + $":{string.Join(',', row.Input.CustomResults.Select(result => result.Id.Value.ToString("N")))}"));

        return OfflineSheetAssembler.Assemble(
            SubmissionModules.ActivityKeyResults, collectionId, organisationId, fingerprint, specs, sheetRows, singleRow: false);
    }

    public static string CustomKey(int slot) => $"own_kr_{slot}";

    public static string CustomNameKey(int slot) => $"own_kr_{slot}_name";

    // What the template shows: the web form's starting values, minus any whose question is hidden by those same
    // values. The activity-record prefill fills an "Other, please specify" box from the record even when the
    // prefilled parent has no "Other" in it, and the web form hides that box; writing it into the file would have the
    // upload reject a value the person never typed.
    private static IReadOnlyDictionary<string, string?> VisibleInitialValues(OfflineActivityInput input, bool transitive)
    {
        var visibility = SubmissionAnswerValidator.Visibility(input.Questions, input.InitialValues, transitive);
        return input.Questions
            .Where(question => visibility.IsVisible(question.Code)
                && input.InitialValues.TryGetValue(question.Code, out var value)
                && !string.IsNullOrWhiteSpace(value))
            .ToDictionary(question => question.Code, question => input.InitialValues[question.Code], StringComparer.Ordinal);
    }

    private static string ScopeNote(ProtocolColumn column)
    {
        var statuses = column.Scope switch
        {
            QuestionStatusScopes.Active => "Active",
            QuestionStatusScopes.ClosedFinished => "Closed or Finished",
            QuestionStatusScopes.Finished => "Finished",
            _ => column.Scope,
        };

        return column.TaskName is null
            ? $"For activities that are {statuses}."
            : $"For ‘{column.TaskName}’ activities that are {statuses}.";
    }

    private static bool IsJointPartnershipKey(string key) => key.EndsWith("@" + JointPartnershipVariant, StringComparison.Ordinal);

    private const string Preamble =
        "How to answer: fill in one row for each activity, from row 4. Do not change column A or row 3 – we use them to "
        + "match your answers to your report. N/A means a question does not apply to that activity, so leave it as it "
        + "is. " + OfflineSheetText.SaveAdvice;

    private sealed record ProtocolColumn(string Key, ResolvedQuestion Prototype, string? TaskName, string Scope, int DisplayOrder);
}