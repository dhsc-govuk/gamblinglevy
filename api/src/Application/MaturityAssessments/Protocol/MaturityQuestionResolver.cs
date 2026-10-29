using System.Security.Cryptography;
using System.Text;

namespace Application.MaturityAssessments.Protocol;

// Flattens the static catalogue into the resolved question list the validator, presenter, snapshot builder and DTOs
// consume. Pure and cached: the catalogue never changes at runtime, so the whole list is built once.
public static class MaturityQuestionResolver
{
    private static readonly Lazy<IReadOnlyList<MaturityResolvedQuestion>> All = new(Build);

    public static IReadOnlyList<MaturityResolvedQuestion> ResolveAll() => All.Value;

    public static IReadOnlyList<MaturityResolvedQuestion> ResolveSection(string sectionCode) =>
        All.Value.Where(q => q.Section == sectionCode).ToList();

    public static IReadOnlyList<MaturityResolvedQuestion> ResolvePage(string sectionCode, string pageCode) =>
        All.Value.Where(q => q.Section == sectionCode && q.Page == pageCode).ToList();

    // ResolvedQuestion.Id is a non-nullable Guid the client keys on; the catalogue has no database ids, so one is
    // derived deterministically from the code. Stable across environments and restarts, unlike a fresh Guid.
    public static Guid IdFor(string code)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(code));
        return new Guid(hash);
    }

    private static IReadOnlyList<MaturityResolvedQuestion> Build()
    {
        var resolved = new List<MaturityResolvedQuestion>();
        var order = 0;

        foreach (var section in MaturityProtocol.Sections)
        {
            foreach (var page in section.Pages)
            {
                foreach (var question in page.Questions)
                {
                    resolved.Add(new MaturityResolvedQuestion(
                        IdFor(question.Code),
                        question.Code,
                        question.Label,
                        question.Hint,
                        question.FieldType,
                        question.IsRequired,
                        order++,
                        question.ConditionalQuestionCode,
                        question.ConditionalValue,
                        question.Options ?? [],
                        question.PrefillSource,
                        question.ErrorMessage ?? DefaultErrorMessage(question),
                        section.Code,
                        MaxLength: null,
                        MaxLengthErrorMessage: null,
                        section.Title,
                        page.Code,
                        page.Title,
                        page.VisibleWhen,
                        question.GroupLabel,
                        question.SecondaryOptions ?? [],
                        question.ExclusiveOptionValue));
                }
            }
        }

        return resolved;
    }

    // GDS wording: a choice is "selected", text is "entered". The shared validator's own fallback says "Enter an
    // answer" for everything, which reads wrong under a radio group.
    private static string DefaultErrorMessage(MaturityQuestionSpec question) =>
        MaturityFieldTypes.IsChoice(question.FieldType)
            ? $"Select an answer for “{question.Label}”"
            : $"Enter an answer for “{question.Label}”";
}