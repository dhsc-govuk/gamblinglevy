using Api.Services.Abstract;
using Api.Services.ReportSnapshot;
using Application.Common.Features;
using Application.Common.Permissions;
using Application.MaturityAssessments.Protocol;
using Application.MaturityAssessments.Snapshots;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;

namespace Api.Services.Implementation;

// Builds the frozen assessment snapshot from the same authorised read side the screens use, applying page and
// question visibility and answer rendering exactly once, so the read-only views and both exports share one source
// of truth for what the finalised assessment contains.
public class MaturityAssessmentSnapshotService(IMaturityAssessmentControllerService controllerService, IFeatureFlags featureFlags)
    : IMaturityAssessmentSnapshotService
{
    public async Task<Option<MaturityAssessmentSnapshotDocument>> BuildAsync(Permission permission, Guid collectionId, CancellationToken cancellationToken)
    {
        // Exactly the sections on the LA's task list — the collection's forms, in catalogue order (BLUM-8093). Walking
        // the whole catalogue would now meet a 404 for every excluded section and give up on the snapshot entirely.
        var taskList = await controllerService.GetTaskListAsync(permission, collectionId, cancellationToken);
        if (taskList.IsNone)
        {
            return Option<MaturityAssessmentSnapshotDocument>.None;
        }

        // BLUM_9087: a page or question whose parent is hidden is left out too.
        var transitive = featureFlags.IsEnabled(Feature.BLUM_9087);
        var sections = new List<MaturitySnapshotSection>();

        foreach (var summary in taskList.ValueUnsafe().Sections)
        {
            var sectionSpec = MaturityProtocol.FindSection(summary.Code);
            if (sectionSpec is null)
            {
                return Option<MaturityAssessmentSnapshotDocument>.None;
            }

            var sectionLookup = await controllerService.GetSectionAsync(permission, collectionId, sectionSpec.Code, cancellationToken);
            if (sectionLookup.IsNone)
            {
                return Option<MaturityAssessmentSnapshotDocument>.None;
            }

            var section = sectionLookup.ValueUnsafe();
            var answers = section.Answers.ToDictionary(a => a.QuestionCode, a => a.Value, StringComparer.Ordinal);
            var questions = MaturityQuestionResolver.ResolveSection(sectionSpec.Code);
            var visibility = MaturityAnswerValidator.Visibility(questions, answers, transitive);

            var pages = new List<MaturitySnapshotPage>();
            foreach (var pageSpec in sectionSpec.Pages)
            {
                if (!MaturityAnswerValidator.IsPageVisible(visibility, pageSpec.VisibleWhen))
                {
                    continue;
                }

                var rows = new List<MaturitySnapshotRow>();
                foreach (var question in questions.Where(q => q.Page == pageSpec.Code).OrderBy(q => q.DisplayOrder))
                {
                    if (!visibility.IsVisible(question.Code))
                    {
                        continue;
                    }

                    answers.TryGetValue(question.Code, out var value);
                    rows.Add(new MaturitySnapshotRow(question.Code, question.Label, Render(question, value), value, question.GroupLabel));
                }

                pages.Add(new MaturitySnapshotPage(pageSpec.Code, pageSpec.Title, rows));
            }

            sections.Add(new MaturitySnapshotSection(sectionSpec.Code, sectionSpec.Title, pages));
        }

        return new MaturityAssessmentSnapshotDocument(sections);
    }

    public string Serialize(MaturityAssessmentSnapshotDocument snapshot) => MaturityAssessmentSnapshotJson.Serialize(snapshot);

    public MaturityAssessmentSnapshotDocument? Deserialize(string json) => MaturityAssessmentSnapshotJson.Deserialize(json);

    private string Render(MaturityResolvedQuestion question, string? value)
    {
        if (question.IsRoles)
        {
            return MaturityRolesAnswer.TryParse(value, out var entries)
                ? MaturityRolesAnswer.Render(entries, question.Options, question.SecondaryOptions, question.ExclusiveOptionValue)
                : string.Empty;
        }

        return ReportAnswerPresenter.RenderAnswer(controllerService.ToReportQuestionDto(question), value);
    }
}