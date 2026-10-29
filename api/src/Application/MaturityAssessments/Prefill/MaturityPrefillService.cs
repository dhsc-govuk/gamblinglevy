using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.MaturityAssessments.Protocol;
using Application.Submissions;
using Domain.Collections;
using Domain.Organisations;
using LanguageExt.UnsafeValueAccess;
using Microsoft.Extensions.Logging;

namespace Application.MaturityAssessments.Prefill;

// Orchestrates the three pre-fill sources for one organisation and one maturity-assessment window. Each source is
// read best-effort in its own try/catch (the ActivityResultCarryForward contract): a broken quarterly read must
// still yield the prior-assessment and baseline rows, and a broken everything must still let the LA start blank.
public class MaturityPrefillService(
    IActivityQueries activityQueries,
    ICategoryQueries categoryQueries,
    IFormSchemaQueries formSchemaQueries,
    ISubmissionQueries submissionQueries,
    IMaturityAssessmentQueries assessmentQueries,
    IMaturityAssessmentAnswerRepository answerRepository,
    IMaturityBaselineQueries baselineQueries,
    ILogger<MaturityPrefillService> logger) : IMaturityPrefillService
{
    public const int BaselineSurveyYear = 2025;

    public async Task<IReadOnlyList<MaturityPrefilledAnswer>> BuildAsync(
        Organisation organisation,
        Collection collection,
        IReadOnlyList<MaturityResolvedQuestion> questions,
        CancellationToken cancellationToken)
    {
        var quarterly = await ReadQuarterlyAsync(organisation, collection, cancellationToken);
        var prior = await ReadPriorAssessmentAsync(organisation, collection, questions, cancellationToken);
        var baseline = await ReadBaselineAsync(organisation, questions, cancellationToken);

        return PrefillMerger.Merge(questions.Select(q => q.Code).ToList(), quarterly, prior, baseline);
    }

    private async Task<IReadOnlyDictionary<string, string>> ReadQuarterlyAsync(
        Organisation organisation,
        Collection collection,
        CancellationToken cancellationToken)
    {
        try
        {
            // A draft has no reporting period and no reports to look back from.
            if (collection.ReportingPeriodEnd is not { } periodEnd)
            {
                return new Dictionary<string, string>();
            }

            var activities = (await activityQueries.GetByOrganisationAsync(organisation.Id, status: null, cancellationToken))
                .Where(ActivityReportContextResolver.IsReportable)
                .ToList();
            if (activities.Count == 0)
            {
                return new Dictionary<string, string>();
            }

            var tasks = (await categoryQueries.GetActiveTasksAsync(cancellationToken)).ToDictionary(t => t.Id, t => t.TaskName);
            var categories = (await categoryQueries.GetActiveCategoriesAsync(cancellationToken)).ToDictionary(c => c.Id, c => c.Name);

            var ids = activities.Select(a => a.Id).ToList();
            var recordAnswers = (await activityQueries.GetAnswersForActivitiesAsync(ids, cancellationToken))
                .GroupBy(a => a.ActivityId)
                .ToDictionary(g => g.Key, g => (IReadOnlyDictionary<string, string>)g.ToDictionary(a => a.QuestionCode, a => a.Value, StringComparer.Ordinal));
            var expectedResults = (await activityQueries.GetExpectedResultsForActivitiesAsync(ids, cancellationToken))
                .Where(r => !r.IsCustom)
                .GroupBy(r => r.ActivityId)
                .ToDictionary(g => g.Key, g => (IReadOnlySet<string>)g.Select(r => r.KeyResultCode!).ToHashSet(StringComparer.Ordinal));

            var facts = activities
                .Select(a => new ActivityFacts(
                    a.Id.Value,
                    a.TaskId is { } taskId && tasks.TryGetValue(taskId, out var taskName) ? taskName : string.Empty,
                    a.CategoryId is { } categoryId && categories.TryGetValue(categoryId, out var categoryName) ? categoryName : string.Empty,
                    a.Status!,
                    a.CustomTaskText,
                    a.CreatedAt,
                    expectedResults.TryGetValue(a.Id, out var codes) ? codes : new HashSet<string>(StringComparer.Ordinal),
                    recordAnswers.TryGetValue(a.Id, out var answers) ? answers : new Dictionary<string, string>(StringComparer.Ordinal)))
                .ToList();

            // Rows arrive most-recent-period-first per activity; only the FIRST submission's rows are kept, so one
            // report's answer set is taken whole rather than gap-filled from older reports (the carry-forward rule).
            var rows = await submissionQueries.GetLatestSubmittedActivityAnswersForOrganisationAsync(organisation.Id, periodEnd, cancellationToken);
            var latestReports = new Dictionary<Guid, LatestReport>();
            foreach (var group in rows.GroupBy(r => r.ActivityId))
            {
                var first = group.First();
                var answers = group
                    .Where(r => r.SubmissionId == first.SubmissionId && !string.IsNullOrWhiteSpace(r.Value))
                    .GroupBy(r => r.QuestionCode, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.First().Value, StringComparer.Ordinal);
                latestReports[group.Key] = new LatestReport(first.SubmissionId, first.ReportingPeriodEnd, answers);
            }

            var whoForLabels = await ReadWhoForLabelsAsync(cancellationToken);

            return QuarterlyPrefillRules.Build(new QuarterlyPrefillInput(facts, latestReports, periodEnd, whoForLabels));
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Quarterly pre-fill failed for organisation {OrganisationId} on collection {CollectionId}; falling back to prior sources.",
                organisation.Id,
                collection.Id);
            return new Dictionary<string, string>();
        }
    }

    private async Task<IReadOnlyDictionary<string, string>> ReadPriorAssessmentAsync(
        Organisation organisation,
        Collection collection,
        IReadOnlyList<MaturityResolvedQuestion> questions,
        CancellationToken cancellationToken)
    {
        try
        {
            if (collection.ReportingPeriodEnd is not { } periodEnd)
            {
                return new Dictionary<string, string>();
            }

            var prior = await assessmentQueries.GetLatestSubmittedBeforeAsync(organisation.Id, periodEnd, cancellationToken);
            if (prior.IsNone)
            {
                return new Dictionary<string, string>();
            }

            var answers = await answerRepository.GetForAssessmentAsync(new Domain.MaturityAssessments.MaturityAssessmentId(prior.ValueUnsafe().AssessmentId), cancellationToken);
            return PriorAssessmentSource.Filter(PriorAssessmentSource.Rows(answers), questions);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Prior-assessment pre-fill failed for organisation {OrganisationId}; falling back to the baseline.",
                organisation.Id);
            return new Dictionary<string, string>();
        }
    }

    private async Task<IReadOnlyDictionary<string, string>> ReadBaselineAsync(
        Organisation organisation,
        IReadOnlyList<MaturityResolvedQuestion> questions,
        CancellationToken cancellationToken)
    {
        try
        {
            var answers = await baselineQueries.GetForOrganisationAsync(organisation.Id, BaselineSurveyYear, cancellationToken);
            return PriorAssessmentSource.Filter(PriorAssessmentSource.Rows(answers), questions);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Baseline pre-fill failed for organisation {OrganisationId}.", organisation.Id);
            return new Dictionary<string, string>();
        }
    }

    // The activity form's "Who was the activity for" option labels, keyed by value — the campaign audience is
    // rendered as labels because the maturity question is free text.
    private async Task<IReadOnlyDictionary<string, string>> ReadWhoForLabelsAsync(CancellationToken cancellationToken)
    {
        var schema = await formSchemaQueries.GetActiveSchemaAsync(cancellationToken);
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var question in schema.Questions.Where(q => q.Code.EndsWith("_WHO_FOR", StringComparison.Ordinal)))
        {
            foreach (var option in question.Options)
            {
                labels.TryAdd(option.Value, option.Label);
            }
        }

        return labels;
    }
}