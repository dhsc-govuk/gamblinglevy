using Application.Common.Interfaces.Queries;
using Domain.DataCollectionForms;

namespace Application.DataCollectionForms.Versions;

// BLUM-9105: what a published version fixes about a form's questions, so answers stay joinable across versions.
// Exports and reports match answers to questions by code and option value, never by row id, so:
//
// - a question code, once published, can never be reused for a different question — even after that question is
//   deleted from a later draft;
// - an option's stored value, once published, can never change or be removed; its label can.
//
// Both read every saved version of the module. A form has a handful, so that is cheap.
public class PublishedQuestionRules(IFormModuleVersionQueries versionQueries)
{
    // Every question code any saved version of the module has held.
    public async Task<IReadOnlySet<string>> PublishedCodesAsync(FormModuleId moduleId, CancellationToken cancellationToken)
    {
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var version in await versionQueries.ListAsync(moduleId, cancellationToken))
        {
            codes.UnionWith(FormModuleDefinition.Parse(version.Definition).Questions.Select(q => q.Code));
        }

        return codes;
    }

    // The option values of this question in the latest version that holds it. Empty when no version has published it.
    public async Task<IReadOnlyList<string>> PublishedOptionValuesAsync(FormModuleId moduleId, string questionCode, CancellationToken cancellationToken) =>
        (await LatestPublishedQuestionsAsync(moduleId, cancellationToken)).TryGetValue(questionCode, out var question)
            ? question.Options.Select(o => o.Value).ToList()
            : [];

    // Every question code any saved version has held, with its definition in the latest version that holds it.
    public async Task<IReadOnlyDictionary<string, FormQuestionDefinition>> LatestPublishedQuestionsAsync(FormModuleId moduleId, CancellationToken cancellationToken)
    {
        var latest = new Dictionary<string, FormQuestionDefinition>(StringComparer.OrdinalIgnoreCase);

        // Newest first, so the first definition seen for a code is the latest.
        foreach (var version in await versionQueries.ListAsync(moduleId, cancellationToken))
        {
            foreach (var question in FormModuleDefinition.Parse(version.Definition).Questions)
            {
                latest.TryAdd(question.Code, question);
            }
        }

        return latest;
    }
}