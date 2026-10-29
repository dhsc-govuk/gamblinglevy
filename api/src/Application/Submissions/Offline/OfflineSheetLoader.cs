using Application.Collections;
using Application.Common.Features;
using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.Submissions.Protocol;
using Domain.Activities;
using Domain.Categories;
using Domain.DataCollectionForms;
using Domain.KeyResults;

namespace Application.Submissions.Offline;

// A section of the report as the offline file sees it: the sheet, plus what validating and writing an upload needs.
public abstract record OfflineSection(OfflineSheet Sheet, QuarterlyReportWriteContext Gate);

public sealed record ActivityResultsOfflineSection(
    OfflineSheet Sheet,
    QuarterlyReportWriteContext Gate,
    IReadOnlyDictionary<string, Activity> ActivitiesByReference)
    : OfflineSection(Sheet, Gate);

public sealed record FundingSpentOfflineSection(
    OfflineSheet Sheet,
    QuarterlyReportWriteContext Gate,
    FundingSpendContext Context,
    IReadOnlyList<int> RemainingQuarters)
    : OfflineSection(Sheet, Gate);

public sealed record StrategicFeedbackOfflineSection(
    OfflineSheet Sheet,
    QuarterlyReportWriteContext Gate,
    StrategicFeedbackContext Context,
    IReadOnlyList<DcFormQuestion> Questions)
    : OfflineSection(Sheet, Gate);

// Loads a section of a report that has passed QuarterlyReportWriteGate and builds its offline sheet. The template
// download and the upload both come through here, so the layout a file was written with and the layout it is
// checked against are built by the same code from the same data. Every question set is resolved exactly as the web
// pages resolve it (KeyResultQuestionResolver, the funding context's two activity sets, the active feedback
// questions), and the values are the ones the web pages start from.
public class OfflineSheetLoader(
    ActivityReportContextResolver activityResolver,
    FundingSpendContextResolver fundingResolver,
    StrategicFeedbackContextResolver feedbackResolver,
    ISubmissionActivityAnswerRepository submissionAnswerRepository,
    ISubmissionSpendEntryRepository spendRepository,
    ISubmissionFeedbackAnswerRepository feedbackRepository,
    IActivityExpectedResultRepository expectedResultRepository,
    IActivityAnswerRepository activityAnswerRepository,
    IKeyResultQueries keyResultQueries,
    CollectionFormModules collectionFormModules,
    ICategoryQueries categoryQueries,
    IFeatureFlags featureFlags)
{
    public async Task<OfflineSection> LoadAsync(QuarterlyReportWriteContext gate, string moduleCode, CancellationToken cancellationToken) =>
        moduleCode switch
        {
            SubmissionModules.ActivityKeyResults => await LoadActivityResultsAsync(gate, cancellationToken),
            SubmissionModules.FundingSpent => await LoadFundingSpentAsync(gate, cancellationToken),
            SubmissionModules.StrategicFeedback => await LoadStrategicFeedbackAsync(gate, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(moduleCode), moduleCode, "Not a section the offline report covers."),
        };

    private async Task<ActivityResultsOfflineSection> LoadActivityResultsAsync(QuarterlyReportWriteContext gate, CancellationToken cancellationToken)
    {
        var activities = await activityResolver.GetReportableActivitiesAsync(gate.OrganisationId, cancellationToken);
        var taskNames = (await categoryQueries.GetActiveTasksAsync(cancellationToken)).ToDictionary(task => task.Id, task => task.TaskName);
        var categories = await categoryQueries.GetActiveCategoriesAsync(cancellationToken);
        var deliveryQuestions = await ActivityReportContextResolver.LoadDeliveryQuestionsAsync(collectionFormModules, gate.Collection, cancellationToken);

        var banks = new Dictionary<ActivityTaskId, IReadOnlyList<KeyResultQuestion>>();
        var inputs = new List<OfflineActivityInput>();
        foreach (var activity in activities)
        {
            IReadOnlyList<KeyResultQuestion> bank = [];
            if (activity.TaskId is not null)
            {
                if (!banks.TryGetValue(activity.TaskId, out var cached))
                {
                    cached = await keyResultQueries.GetQuestionsForTaskAsync(activity.TaskId, cancellationToken);
                    banks[activity.TaskId] = cached;
                }

                bank = cached;
            }

            var taskName = activity.TaskId is not null && taskNames.TryGetValue(activity.TaskId, out var name) ? name : null;
            var isJointPartnership = ActivityReportContextResolver.IsJointPartnership(categories, activity);
            var expectedResults = await expectedResultRepository.GetForActivityAsync(activity.Id, cancellationToken);
            var questions = KeyResultQuestionResolver.Resolve(activity, expectedResults, bank, deliveryQuestions, isJointPartnership);

            var activityAnswers = await activityAnswerRepository.GetForActivityAsync(activity.Id, cancellationToken);
            var saved = await submissionAnswerRepository.GetForActivityAsync(gate.Submission.Id, activity.Id, cancellationToken);

            inputs.Add(new OfflineActivityInput(
                activity,
                ActivityDisplayTitle.Resolve(activity, taskName),
                taskName,
                isJointPartnership,
                questions,
                expectedResults.Where(result => result.IsCustom).OrderBy(result => result.DisplayOrder).ThenBy(result => result.Id.Value).ToList(),
                KeyResultQuestionResolver.InitialValues(questions, activityAnswers, saved)));
        }

        var protocolById = banks.Values
            .SelectMany(bank => bank)
            .GroupBy(question => question.Id.Value)
            .ToDictionary(group => group.Key, group => group.First());

        var sheet = ActivityResultsSheetBuilder.Build(
            gate.Collection.Id.Value, gate.OrganisationId.Value, inputs, protocolById, featureFlags.IsEnabled(Feature.BLUM_9087));
        var byReference = activities.ToDictionary(activity => activity.Id.Value.ToString(), activity => activity, StringComparer.Ordinal);

        return new ActivityResultsOfflineSection(sheet, gate, byReference);
    }

    private async Task<FundingSpentOfflineSection> LoadFundingSpentAsync(QuarterlyReportWriteContext gate, CancellationToken cancellationToken)
    {
        var context = await fundingResolver.ForAsync(gate, cancellationToken);
        var collection = gate.Collection;
        var remaining = ReportingPeriod.RemainingQuarters(collection.Quarter!.Value);

        var taskNames = (await categoryQueries.GetActiveTasksAsync(cancellationToken)).ToDictionary(task => task.Id, task => task.TaskName);
        var titles = context.ForecastSpendActivities.ToDictionary(
            activity => activity.Id.Value,
            activity => ActivityDisplayTitle.Resolve(
                activity,
                activity.TaskId is not null && taskNames.TryGetValue(activity.TaskId, out var name) ? name : null));

        var quarterPeriods = remaining.ToDictionary(quarter => quarter, quarter => ReportingPeriod.Derive(collection.FinancialYear!, quarter));
        var entries = await spendRepository.GetForSubmissionAsync(gate.Submission.Id, cancellationToken);

        var sheet = FundingSpentSheetBuilder.Build(
            collection.Id.Value,
            gate.OrganisationId.Value,
            context,
            remaining,
            titles,
            (collection.ReportingPeriodStart!.Value, collection.ReportingPeriodEnd!.Value),
            quarterPeriods,
            entries);

        return new FundingSpentOfflineSection(sheet, gate, context, remaining);
    }

    private async Task<StrategicFeedbackOfflineSection> LoadStrategicFeedbackAsync(QuarterlyReportWriteContext gate, CancellationToken cancellationToken)
    {
        var context = StrategicFeedbackContextResolver.For(gate);
        var questions = await feedbackResolver.GetQuestionsAsync(gate.Collection, cancellationToken);
        var saved = await feedbackRepository.GetForSubmissionAsync(gate.Submission.Id, cancellationToken);

        var sheet = StrategicFeedbackSheetBuilder.Build(gate.Collection.Id.Value, gate.OrganisationId.Value, gate.Organisation.Name, questions, saved);
        return new StrategicFeedbackOfflineSection(sheet, gate, context, questions);
    }
}