using Application.Activities.Audit;
using Application.Activities.Exceptions;
using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.Forms.Answers;
using Application.Submissions;
using Domain.Activities;
using Domain.AuditLogs;
using Domain.Categories;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using MediatR;

namespace Application.Activities.Commands.UpdateActivity;

public record UpdateActivityCommand : IRequest<Either<ActivityException, Activity>>
{
    public Guid Id { get; init; }
    public string? Title { get; init; }
    public Guid? CategoryId { get; init; }
    public Guid? TaskId { get; init; }
    public string? CustomTaskText { get; init; }
    public string? Status { get; init; }
    public int? StartMonth { get; init; }
    public int? StartYear { get; init; }
    public IReadOnlyList<ExpectedResultInput> ExpectedResults { get; init; } = [];
    public IReadOnlyList<ActivityAnswerInput> Answers { get; init; } = [];
    public bool SaveAsDraft { get; init; }
}

public class UpdateActivityCommandHandler(
    IActivityRepository activityRepository,
    IActivityExpectedResultRepository activityExpectedResultRepository,
    IActivityAnswerRepository activityAnswerRepository,
    IFormSchemaQueries formSchemaQueries,
    IActivityQueries activityQueries,
    ICategoryQueries categoryQueries,
    IKeyResultQueries keyResultQueries,
    IAuditLogRepository auditLogRepository,
    SubmissionReviewService submissionReviewService,
    PermissionsService permissionsService)
    : IRequestHandler<UpdateActivityCommand, Either<ActivityException, Activity>>
{
    public async Task<Either<ActivityException, Activity>> Handle(
        UpdateActivityCommand request,
        CancellationToken cancellationToken)
    {
        return await permissionsService.GetOrganisationPermissions().MatchAsync<Either<ActivityException, Activity>>(
            permission => HandleWithPermission(request, permission, cancellationToken),
            error => new ActivityForbiddenException(new ActivityId(request.Id), error.Message));
    }

    private async Task<Either<ActivityException, Activity>> HandleWithPermission(
        UpdateActivityCommand request,
        Permission permission,
        CancellationToken cancellationToken)
    {
        if (permission.OrganisationId.IsNone || permission.UserId.IsNone)
        {
            return new ActivityForbiddenException(new ActivityId(request.Id), "You are not associated with an organisation.");
        }

        var organisationId = permission.OrganisationId.ValueUnsafe();
        var actorId = permission.UserId.ValueUnsafe();
        var activityId = new ActivityId(request.Id);

        var lookup = await activityQueries.GetByIdForOrgAsync(activityId, organisationId, cancellationToken: cancellationToken);
        if (lookup.IsNone)
        {
            return new ActivityDoesNotExistException(activityId);
        }

        var activity = lookup.ValueUnsafe();

        // Closed/Finished completed records are read-only.
        if (activity.IsComplete && activity.Status is not null && activity.Status.IsReadOnly)
        {
            return new ActivityNotEditableException(activityId, activity.Status.ToString());
        }

        // Only a record that is still a draft can be (re)saved as a draft.
        var savingAsDraft = request.SaveAsDraft && !activity.IsComplete;
        var completing = !activity.IsComplete && !request.SaveAsDraft;

        var requestCategoryId = request.CategoryId.HasValue ? new CategoryId(request.CategoryId.Value) : null;
        var requestTaskId = request.TaskId.HasValue ? new ActivityTaskId(request.TaskId.Value) : null;
        var status = string.IsNullOrWhiteSpace(request.Status) ? null : ActivityStatus.From(request.Status);
        var startMonth = status?.IsActive == true ? request.StartMonth : null;
        var startYear = status?.IsActive == true ? request.StartYear : null;

        // Category and type are locked once setup is complete, so the effective category for schema scoping and
        // completion is the existing category for completed records, or the (possibly changed) one for drafts.
        var effectiveCategoryId = activity.IsComplete ? activity.CategoryId : requestCategoryId;
        var effectiveTaskId = activity.IsComplete ? activity.TaskId : requestTaskId;

        // Required answers are only enforced for questions applicable to the activity's type (sections scoped
        // to another type never block); type/option errors block for every schema question, and every valid
        // schema answer is persisted — so answers submitted for a de-scoped section (e.g. by a stale client)
        // are kept rather than silently dropped.
        var schema = await formSchemaQueries.GetActiveSchemaForCategoryAsync(effectiveCategoryId, cancellationToken);
        var applicableQuestions = schema.QuestionsForTask(effectiveTaskId);
        var inapplicableQuestions = schema.Questions.Except(applicableQuestions).ToList();
        var answersMap = BuildAnswersMap(request.Answers);
        var answerErrors = ActivityAnswerValidator.Validate(applicableQuestions, answersMap, !savingAsDraft)
            .Concat(ActivityAnswerValidator.Validate(inapplicableQuestions, answersMap, enforceRequired: false))
            .ToList();
        if (answerErrors.Count > 0)
        {
            return new ActivityAnswersInvalidException(activityId, answerErrors);
        }

        var persistableAnswers = ActivityAnswerValidator.CollectPersistable(schema.Questions, answersMap);

        // Key results: standard selections must belong to the activity's type; the per-category minimum is
        // enforced for every non-draft save (never for People-and-roles, which has no key results).
        var definitions = effectiveTaskId is null
            ? []
            : await keyResultQueries.GetDefinitionsForTaskAsync(effectiveTaskId, cancellationToken);
        var (expectedResultRows, expectedResultsError) = ExpectedResultInputs.Normalise(request.ExpectedResults, definitions);
        if (expectedResultsError is not null)
        {
            return new ActivityExpectedResultsInvalidException(activityId, expectedResultsError);
        }

        if (!savingAsDraft)
        {
            var minimumError = await ExpectedResultInputs.ValidateMinimumAsync(
                categoryQueries, effectiveCategoryId, definitions.Count, expectedResultRows.Count, cancellationToken);
            if (minimumError is not null)
            {
                return new ActivityExpectedResultsInvalidException(activityId, minimumError);
            }
        }

        var existingExpectedResults = await activityExpectedResultRepository.GetForActivityAsync(activityId, cancellationToken);
        var before = ActivitySnapshot.Serialize(activity, existingExpectedResults);

        try
        {
            activity.UpdateDetails(request.Title, null);

            if (!activity.IsComplete)
            {
                activity.ChangeCategoryAndType(requestCategoryId, requestTaskId, request.CustomTaskText);
            }

            if (status is not null)
            {
                activity.SetStatus(status, startMonth, startYear);
            }

            if (completing)
            {
                var missing = CollectMissingFields(activity);
                if (missing.Count > 0)
                {
                    return new ActivityIncompleteException(activityId, missing);
                }

                activity.CompleteSetup();
            }

            var saved = await activityRepository.UpdateAsync(activity, cancellationToken);

            await SyncExpectedResultsAsync(activityId, existingExpectedResults, expectedResultRows, actorId, cancellationToken);

            // Replace only the answers this request can legitimately assert about: the applicable questions
            // (cleared/hidden ones are pruned) plus whatever it explicitly submitted. Answers for sections
            // scoped to a different type — and orphaned answers whose question was since deleted — survive.
            var replacedCodes = applicableQuestions.Select(q => q.Code)
                .Concat(persistableAnswers.Select(p => p.Question.Code))
                .ToHashSet(StringComparer.Ordinal);
            await activityAnswerRepository.RemoveByActivityAsync(activityId, replacedCodes, cancellationToken);
            foreach (var (question, value) in persistableAnswers)
            {
                await activityAnswerRepository.AddAsync(
                    ActivityAnswer.Record(ActivityAnswerId.New(), activityId, question.Id, question.Code, value, actorId),
                    cancellationToken);
            }

            var savedExpectedResults = await activityExpectedResultRepository.GetForActivityAsync(activityId, cancellationToken);
            await auditLogRepository.AppendAsync(
                AuditLog.Create(
                    actorId,
                    AuditTargetType.Activity,
                    saved.Id.Value,
                    completing ? AuditAction.ActivityCompleted : AuditAction.ActivityUpdated,
                    before: before,
                    after: ActivitySnapshot.Serialize(saved, savedExpectedResults),
                    organisationId: organisationId),
                cancellationToken);

            // Changes to a completed record (status, key results, completion itself) alter what the quarterly
            // report asks — in-flight reports must be reviewed (BLUM-6611). Draft edits don't affect the list.
            if (saved.IsComplete)
            {
                await submissionReviewService.FlagOpenSubmissionsAsync(organisationId, cancellationToken);
            }

            return saved;
        }
        catch (Exception exception)
        {
            return new ActivityUnknownException(activityId, exception);
        }
    }

    private static List<string> CollectMissingFields(Activity activity)
    {
        var missing = new List<string>();
        if (activity.CategoryId is null)
        {
            missing.Add("category");
        }

        if (activity.TaskId is null)
        {
            missing.Add("type");
        }

        if (activity.Status is null)
        {
            missing.Add("status");
        }

        return missing;
    }

    // Diff/merge instead of delete-and-recreate: expected-result row ids are embedded in submission answer
    // codes (CUSTOM_{id:N}), so recreating rows orphans saved quarterly-report answers (BLUM-6805).
    private async Task SyncExpectedResultsAsync(
        ActivityId activityId,
        IReadOnlyList<ActivityExpectedResult> existing,
        IReadOnlyList<(string? KeyResultCode, string Text)> expectedResults,
        Domain.Users.UserId actorId,
        CancellationToken cancellationToken)
    {
        var plan = ExpectedResultSync.Plan(existing, expectedResults, activityId, actorId);

        if (plan.ToRemove.Count > 0)
        {
            await activityExpectedResultRepository.RemoveRangeAsync(plan.ToRemove, cancellationToken);
        }

        foreach (var amendment in plan.ToUpdate)
        {
            amendment.Row.Amend(amendment.Text, amendment.DisplayOrder);
            await activityExpectedResultRepository.UpdateAsync(amendment.Row, cancellationToken);
        }

        foreach (var row in plan.ToAdd)
        {
            await activityExpectedResultRepository.AddAsync(row, cancellationToken);
        }
    }

    private static Dictionary<string, string?> BuildAnswersMap(IReadOnlyList<ActivityAnswerInput> answers)
    {
        var map = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var answer in answers)
        {
            if (!string.IsNullOrWhiteSpace(answer.QuestionCode))
            {
                map[answer.QuestionCode.Trim()] = answer.Value;
            }
        }

        return map;
    }
}