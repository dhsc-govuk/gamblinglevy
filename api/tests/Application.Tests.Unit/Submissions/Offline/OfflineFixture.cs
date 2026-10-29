using System.Text;
using Application.Common.Csv;
using Application.Submissions;
using Application.Submissions.Offline;
using Application.Submissions.Protocol;
using Domain.Activities;
using Domain.Categories;
using Domain.DataCollectionForms;
using Domain.Forms;
using Domain.KeyResults;
using Domain.Organisations;
using Domain.Submissions;
using Domain.Users;
using Infrastructure.Persistence.Seed;

namespace Application.Tests.Unit.Submissions.Offline;

// Builds offline sheets from the real seeded protocol (KeyResultProtocolSeedData), the same transcription the
// database is seeded from, so the column model and guidance are tested against the questions LAs actually get.
public static class OfflineFixture
{
    public const string RnaTaskName = "Contributing to a regional needs assessment";
    public const string SteeringGroupTaskName = "Setting up and running a local steering group";

    // A results question set no seeded type has: CHAIN_A → CHAIN_B → CHAIN_C, each revealed by "yes" on the one before.
    // Every seeded rule is one level deep, so this is the only way to reach a grandchild (BLUM-9087 spike item 7). C's
    // column comes before B's on purpose, so an upload check cannot rely on meeting a parent before its follow-up.
    public const string ChainTaskName = "Synthetic chain of follow-ups";
    public const string ChainKeyResult = "chain_kr";

    public static readonly Guid ReportCollectionId = Guid.Parse("c0000000-0000-0000-0000-000000000001");
    public static readonly Guid ReportOrganisationId = Guid.Parse("d0000000-0000-0000-0000-000000000001");

    private static readonly UserId Actor = UserId.New();

    private static readonly Dictionary<string, (ActivityTaskId TaskId, IReadOnlyList<KeyResultQuestion> Bank)> Banks = new();

    public static (ActivityTaskId TaskId, IReadOnlyList<KeyResultQuestion> Bank) Bank(string taskName)
    {
        lock (Banks)
        {
            if (Banks.TryGetValue(taskName, out var cached))
            {
                return cached;
            }

            var taskId = ActivityTaskId.New();
            if (taskName == ChainTaskName)
            {
                Banks[taskName] = (taskId, ChainBank(taskId));
                return Banks[taskName];
            }

            var type = KeyResultProtocolSeedData.Protocol.Single(t => t.TaskName == taskName);
            var order = 0;
            var bank = type.Questions
                .Select(question => KeyResultQuestion.Seed(
                    KeyResultQuestionId.New(),
                    taskId,
                    question.KeyResultCode,
                    question.Code,
                    QuestionStatusScope.From(question.StatusScope),
                    question.Label,
                    ProtocolFieldType.From(question.FieldType),
                    order++,
                    Actor,
                    hint: question.Hint,
                    minValue: question.Min,
                    maxValue: question.Max,
                    options: question.Options,
                    prefillSource: question.PrefillSource,
                    conditionalQuestionCode: question.ConditionalQuestionCode,
                    conditionalValue: question.ConditionalValue,
                    errorMessage: question.ErrorMessage,
                    utcNow: DateTime.UtcNow))
                .ToList();

            Banks[taskName] = (taskId, bank);
            return (taskId, bank);
        }
    }

    private static IReadOnlyList<KeyResultQuestion> ChainBank(ActivityTaskId taskId) =>
    [
        ChainQuestion(taskId, "CHAIN_A", "Is the first step done?", ProtocolFieldTypes.Boolean, 0, parent: null),
        ChainQuestion(taskId, "CHAIN_B", "Is the second step done?", ProtocolFieldTypes.Boolean, 2, parent: "CHAIN_A"),
        ChainQuestion(taskId, "CHAIN_C", "Tell us about the third step", ProtocolFieldTypes.Text, 1, parent: "CHAIN_B"),
    ];

    private static KeyResultQuestion ChainQuestion(ActivityTaskId taskId, string code, string label, string fieldType, int order, string? parent) =>
        KeyResultQuestion.Seed(
            KeyResultQuestionId.New(),
            taskId,
            ChainKeyResult,
            code,
            QuestionStatusScope.From(QuestionStatusScopes.Active),
            label,
            ProtocolFieldType.From(fieldType),
            order,
            Actor,
            DateTime.UtcNow,
            conditionalQuestionCode: parent,
            conditionalValue: parent is null ? null : "yes");

    public static IReadOnlyList<DcFormQuestion> DeliveryQuestions()
    {
        var module = FormModule.Create(FormModuleId.New(), SubmissionModules.ActivityKeyResults, "Enter activity results", 1, Actor, DateTime.UtcNow);
        return
        [
            module.AddQuestion(DcFormQuestionId.New(), "AKR_DELIVERY_PROGRESS", "What progress have you made?", QuestionType.Textarea, 0, Actor, isRequired: true, maxLength: 500, utcNow: DateTime.UtcNow),
            module.AddQuestion(DcFormQuestionId.New(), "AKR_DELIVERY_NEXT_QUARTER", "What will you do next quarter?", QuestionType.Textarea, 1, Actor, maxLength: 500, utcNow: DateTime.UtcNow),
        ];
    }

    public sealed record ActivitySpec(
        string TaskName,
        ActivityStatus Status,
        string Title,
        string[] StandardResults,
        string[]? CustomResults = null,
        IReadOnlyDictionary<string, string?>? InitialValues = null,
        ActivityId? Id = null);

    public static (OfflineSheet Sheet, IReadOnlyList<OfflineActivityInput> Inputs) ActivityResults(params ActivitySpec[] specs)
    {
        var delivery = DeliveryQuestions();
        var inputs = new List<OfflineActivityInput>();
        var protocol = new Dictionary<Guid, KeyResultQuestion>();

        foreach (var spec in specs)
        {
            var (taskId, bank) = Bank(spec.TaskName);
            foreach (var question in bank)
            {
                protocol[question.Id.Value] = question;
            }

            var activity = Activity.NewComplete(
                spec.Id ?? ActivityId.New(), new OrganisationId(ReportOrganisationId), Actor, CategoryId.New(), taskId, spec.Status, title: spec.Title, utcNow: DateTime.UtcNow);
            var order = 0;
            var results = spec.StandardResults
                .Select(code => ActivityExpectedResult.Record(ActivityExpectedResultId.New(), activity.Id, code, order++, Actor, DateTime.UtcNow, code))
                .Concat((spec.CustomResults ?? []).Select(text => ActivityExpectedResult.Record(ActivityExpectedResultId.New(), activity.Id, text, order++, Actor, DateTime.UtcNow)))
                .ToList();

            var questions = KeyResultQuestionResolver.Resolve(activity, results, bank, delivery);
            inputs.Add(new OfflineActivityInput(
                activity,
                spec.Title,
                spec.TaskName,
                IsJointPartnership: false,
                questions,
                results.Where(result => result.IsCustom).ToList(),
                spec.InitialValues ?? new Dictionary<string, string?>()));
        }

        return (ActivityResultsSheetBuilder.Build(ReportCollectionId, ReportOrganisationId, inputs, protocol, transitive: false), inputs);
    }

    // The template as a person would edit it: parse it, set cells by (row reference, column key), save it again.
    public static byte[] Edit(byte[] template, params (string Reference, string ColumnKey, string Value)[] edits)
    {
        var records = CsvRecordReader.Read(CsvTextDecoder.Decode(template)).Select(record => record.Cells.ToArray()).ToList();
        var keys = records[2];
        foreach (var (reference, columnKey, value) in edits)
        {
            var row = records.First(cells => cells[0] == reference);
            row[Array.IndexOf(keys, columnKey)] = value;
        }

        return CsvWriter.Build(records[0], records.Skip(1).Select(cells => (IReadOnlyList<string>)cells), byteOrderMark: true);
    }

    public static byte[] RemoveRow(byte[] template, string reference)
    {
        var records = CsvRecordReader.Read(CsvTextDecoder.Decode(template)).Select(record => record.Cells).ToList();
        records.RemoveAll(cells => cells[0] == reference);
        return CsvWriter.Build(records[0], records.Skip(1), byteOrderMark: true);
    }

    public static IReadOnlyList<CsvRecord> Records(byte[] file) => CsvRecordReader.Read(CsvTextDecoder.Decode(file));

    public static UploadedOfflineFile Upload(byte[] file)
    {
        var (upload, problem) = OfflineFileReader.Read(file);
        if (upload is null)
        {
            throw new InvalidOperationException($"The file could not be read: {problem}.");
        }

        return upload;
    }

    public static OfflineValidationResult ValidateActivityResults(OfflineSheet sheet, byte[] file, bool transitive = false) =>
        OfflineUploadValidator.Validate(sheet, Upload(file), rows => OfflineSectionRules.ActivityResults(rows, transitive), transitive);

    public static string Utf8(byte[] content) => Encoding.UTF8.GetString(content);
}