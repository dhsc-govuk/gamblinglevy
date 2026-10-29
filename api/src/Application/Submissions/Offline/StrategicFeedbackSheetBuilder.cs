using Application.Submissions.Protocol;
using Domain.DataCollectionForms;
using Domain.Submissions;

namespace Application.Submissions.Offline;

// "Provide strategic feedback" as a spreadsheet: the questions are about the organisation, not an activity, so the
// file has a single answer row with one column per question.
public static class StrategicFeedbackSheetBuilder
{
    public const string ReferenceKey = "organisation";
    public const string ErrorsKey = "errors";

    private const string Section = "feedback";

    public static OfflineSheet Build(
        Guid collectionId,
        Guid organisationId,
        string organisationName,
        IReadOnlyList<DcFormQuestion> questions,
        IReadOnlyList<SubmissionFeedbackAnswer> savedAnswers)
    {
        var resolved = questions.Select(ToResolved).ToList();

        var specs = new List<OfflineColumnSpec>
        {
            new(ReferenceKey, OfflineColumnKind.Reference, "Organisation (do not change)", Guidance: Preamble),
            new(ErrorsKey, OfflineColumnKind.Errors, OfflineSheetText.ErrorsHeading, Guidance: OfflineSheetText.ErrorsGuidance),
        };
        specs.AddRange(resolved.Select(question => new OfflineColumnSpec(question.Code, OfflineColumnKind.Question, question.Label, question)));

        var codes = resolved.Select(question => question.Code).ToHashSet(StringComparer.Ordinal);
        var values = savedAnswers
            .Where(answer => codes.Contains(answer.QuestionCode))
            .GroupBy(answer => answer.QuestionCode, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (string?)group.Last().Value, StringComparer.Ordinal);

        var row = new OfflineRow(
            organisationName,
            "Your answers",
            new Dictionary<string, string>(StringComparer.Ordinal),
            resolved.Select(question => new OfflineBinding(question.Code, question)).ToList(),
            values);

        var fingerprint = OfflineIdentity.ComputeFingerprint(codes);

        return OfflineSheetAssembler.Assemble(
            SubmissionModules.StrategicFeedback, collectionId, organisationId, fingerprint, specs, [row], singleRow: true);
    }

    // The feedback question in the shape the cell reader and the guidance work from. Only active options: they are
    // the answers StrategicFeedbackAnswerValidator accepts.
    public static ResolvedQuestion ToResolved(DcFormQuestion question) => new(
        question.Id.Value,
        question.Code,
        question.Label,
        question.Hint,
        question.QuestionType.ToString(),
        question.IsRequired,
        question.DisplayOrder,
        null,
        null,
        question.Options
            .Where(option => option.IsActive)
            .OrderBy(option => option.DisplayOrder)
            .Select(option => new ResolvedQuestionOption(option.Value, option.Label))
            .ToList(),
        null,
        null,
        IsPercentage: false,
        null,
        question.ErrorMessage,
        Section,
        question.MaxLength,
        question.MaxLengthErrorMessage);

    private const string Preamble =
        "How to answer: answer each question in row 4. Do not change row 3 – we use it to match your answers to your "
        + "report. " + OfflineSheetText.SaveAdvice;
}