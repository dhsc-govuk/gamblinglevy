using Api.Dtos;
using Api.Services.Abstract;
using Api.Services.ActivityRecord;
using Api.Services.ReportSnapshot;
using Application.Common.Csv;
using Application.Common.Features;
using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.Submissions;
using Application.Submissions.Protocol;
using Domain.Activities;
using Domain.Categories;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;

namespace Api.Services.Implementation;

// The "Download activity record" file (BLUM-7648): one CSV restating the whole activity record — every field the LA
// entered, not only the ones the quarterly report pulls through — followed by what each submitted quarterly report
// recorded for it. The record half reads through the same three payloads the activity page renders from, so the file
// and the page cannot disagree about which questions exist; the reported half reads the live answer and spend rows of
// finalised reports, because the frozen report snapshot keys activities by title alone and cannot be traced back to
// one activity. Read-only and unaudited, like the LA's own report download.
public class ActivityRecordExportService(
    IActivityQueries activityQueries,
    IActivitiesControllerService activitiesControllerService,
    IFormDesignerControllerService formDesignerControllerService,
    IOrganisationQueries organisationQueries,
    ISubmissionQueries submissionQueries,
    IActivityExpectedResultRepository expectedResultRepository,
    IKeyResultQueries keyResultQueries,
    CollectionFormModules collectionFormModules,
    IFeatureFlags featureFlags) : IActivityRecordExportService
{
    // The identity columns lead every row, as in every other LA export (BLUM-7647): a downloaded file gets moved and
    // mailed on, and a row that does not say whose record it is stops being usable once that happens.
    private static readonly string[] Header = ["Local authority", "Activity", "Report", "Section", "Question", "Answer"];

    public async Task<Option<ReportExportFile>> GenerateAsync(Permission permission, Guid activityId, CancellationToken cancellationToken)
    {
        // IsNone/ValueUnsafe, not Match — Option.Match throws on a null result (BLUM-7216).
        var organisationId = permission.OrganisationId.IsNone ? null : permission.OrganisationId.ValueUnsafe();
        if (organisationId is null)
        {
            return Option<ReportExportFile>.None;
        }

        var activityLookup = await activityQueries.GetByIdForOrgAsync(new ActivityId(activityId), organisationId, cancellationToken: cancellationToken);
        if (activityLookup.IsNone)
        {
            return Option<ReportExportFile>.None;
        }

        var organisationLookup = await organisationQueries.GetOrganisationById(organisationId, cancellationToken);
        if (organisationLookup.IsNone)
        {
            return Option<ReportExportFile>.None;
        }

        var activity = activityLookup.ValueUnsafe();
        var organisationName = organisationLookup.ValueUnsafe().Name;
        var dto = await activitiesControllerService.ToDtoAsync(activity, cancellationToken);
        var taxonomy = await activitiesControllerService.GetTaxonomyAsync(cancellationToken);

        // The page loads no schema for a record without a category, so it shows no dynamic sections either.
        var schema = dto.CategoryId is null
            ? new FormSchemaDto([], [])
            : await formDesignerControllerService.GetActiveSchemaForCategoryAsync(dto.CategoryId, cancellationToken);

        var rows = new List<ActivityRecordRow>(ActivityRecordPresenter.CreationRows(dto, schema, taxonomy, featureFlags.IsEnabled(Feature.BLUM_9087)));
        rows.AddRange(await BuildReportRowsAsync(activity, organisationId, dto.CategoryName == CategoryNames.JointPartnership, cancellationToken));

        var content = CsvWriter.Build(
            Header,
            rows.Select(row => (IReadOnlyList<string>)[organisationName, dto.Title, row.Report, row.Section, row.Question, row.Answer]));

        return new ReportExportFile(BuildFileName(organisationName, dto.Title), content);
    }

    private async Task<IReadOnlyList<ActivityRecordRow>> BuildReportRowsAsync(
        Activity activity,
        Domain.Organisations.OrganisationId organisationId,
        bool isJointPartnership,
        CancellationToken cancellationToken)
    {
        var answers = await submissionQueries.GetSubmittedAnswersForActivityAsync(organisationId, activity.Id, cancellationToken);
        var spend = await submissionQueries.GetSubmittedSpendForActivityAsync(organisationId, activity.Id, cancellationToken);
        if (answers.Count == 0 && spend.Count == 0)
        {
            return [];
        }

        // Labelling an answer needs the protocol, which only an activity that reported results pays to load.
        IReadOnlyList<ActivityExpectedResult> expectedResults = [];
        IReadOnlyList<Domain.KeyResults.KeyResultQuestion> questionBank = [];
        IReadOnlyList<Domain.DataCollectionForms.DcFormQuestion> deliveryQuestions = [];
        if (answers.Count > 0)
        {
            expectedResults = await expectedResultRepository.GetForActivityAsync(activity.Id, cancellationToken);
            questionBank = activity.TaskId is null ? [] : await keyResultQueries.GetQuestionsForTaskAsync(activity.TaskId, cancellationToken);
            deliveryQuestions = await ActivityReportContextResolver.LoadDeliveryQuestionsAsync(collectionFormModules, null, cancellationToken);
        }

        // Both queries arrive oldest report first; a report that recorded spend but no results answers (a Planned
        // activity's forecast, say) still gets its block.
        var reports = answers.Select(answer => answer.Report)
            .Concat(spend.Select(entry => entry.Report))
            .DistinctBy(report => report.SubmissionId)
            .OrderBy(report => report.ReportingPeriodEnd is null)
            .ThenBy(report => report.ReportingPeriodEnd)
            .ThenBy(report => report.SubmittedAt)
            .ToList();

        var rows = new List<ActivityRecordRow>();
        foreach (var report in reports)
        {
            var reportAnswers = answers
                .Where(answer => answer.Report.SubmissionId == report.SubmissionId)
                .GroupBy(answer => answer.QuestionCode, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().Value, StringComparer.Ordinal);
            var answered = reportAnswers.Count == 0
                ? []
                : KeyResultQuestionResolver.ResolveAnswered(activity, expectedResults, questionBank, deliveryQuestions, reportAnswers, isJointPartnership);

            rows.AddRange(ActivityRecordPresenter.ReportRows(
                report,
                answered,
                spend.Where(entry => entry.Report.SubmissionId == report.SubmissionId).ToList()));
        }

        return rows;
    }

    // "Manchester-City-Council-Community-outreach-activity-record.csv" — the organisation and the activity, each through
    // the one slug rule every export file name uses, each dropped when it slugs away to nothing.
    private static string BuildFileName(string organisationName, string activityTitle)
    {
        var segments = new[] { ExportFileName.NameSegment(organisationName), ExportFileName.NameSegment(activityTitle) }
            .Where(segment => segment.Length > 0)
            .Append("activity-record");
        return $"{string.Join("-", segments)}.csv";
    }
}