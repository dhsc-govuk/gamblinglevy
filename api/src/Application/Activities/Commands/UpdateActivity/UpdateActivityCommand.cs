using Application.Activities.Audit;
using Application.Activities.Exceptions;
using Application.Common.Features;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.Forms.Answers;
using Application.Submissions;
using Domain.Activities;
using Domain.AuditLogs;
using Domain.Categories;
using FluentValidation;
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
    public string? ClosureReason { get; init; }
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
    ITransactionRunner transactionRunner,
    SubmissionReviewService submissionReviewService,
    PermissionsService permissionsService,
    IValidator<UpdateActivityCommand> validator,
    IDateTimeProvider dateTimeProvider,
    IFeatureFlags featureFlags)
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

        // The validator skipped its completion rules because the request claimed SaveAsDraft, but a completed record
        // is saved for real whatever the flag says — so run them as the save this is. Otherwise a direct PUT with
        // saveAsDraft: true could close the record without a reason (BLUM-7673), which being read-only it could then
        // never be given, or store a missing or impossible start date. Thrown, not returned, so the refusal is the
        // same validation_failed envelope a saveAsDraft: false request gets.
        if (activity.IsComplete && request.SaveAsDraft)
        {
            var completion = await validator.ValidateAsync(request with { SaveAsDraft = false }, cancellationToken);
            if (!completion.IsValid)
            {
                throw new ValidationException(completion.Errors);
            }
        }

        var completing = !activity.IsComplete && !request.SaveAsDraft;

        var requestCategoryId = request.CategoryId.HasValue ? new CategoryId(request.CategoryId.Value) : null;
        var requestTaskId = request.TaskId.HasValue ? new ActivityTaskId(request.TaskId.Value) : null;
        var status = string.IsNullOrWhiteSpace(request.Status) ? null : ActivityStatus.From(request.Status);
        var startMonth = status?.IsActive == true ? request.StartMonth : null;
        var startYear = status?.IsActive == true ? request.StartYear : null;
        var closureReason = status?.IsClosed == true ? request.ClosureReason : null;

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

        // One visibility over the whole schema for both halves and the save, so a rule that points across the split by
        // activity type is judged the same way by each (BLUM_9087 makes it transitive).
        var visibility = ActivityAnswerValidator.Visibility(schema.Questions, answersMap, featureFlags.IsEnabled(Feature.BLUM_9087));
        var answerErrors = ActivityAnswerValidator.Validate(applicableQuestions, answersMap, !savingAsDraft, visibility)
            .Concat(ActivityAnswerValidator.Validate(inapplicableQuestions, answersMap, enforceRequired: false, visibility))
            .ToList();
        if (answerErrors.Count > 0)
        {
            return new ActivityAnswersInvalidException(activityId, answerErrors);
        }

        var persistableAnswers = ActivityAnswerValidator.CollectPersistable(schema.Questions, answersMap, visibility);

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
            var (nameError, minimumError) = await ExpectedResultInputs.ValidateCompletionAsync(
                categoryQueries, effectiveCategoryId, request.Title, definitions.Count, expectedResultRows.Count, cancellationToken);
            if (nameError is not null)
            {
                return new ActivityNameRequiredException(activityId, nameError);
            }

            if (minimumError is not null)
            {
                return new ActivityExpectedResultsInvalidException(activityId, minimumError);
            }
        }

        var existingExpectedResults = await activityExpectedResultRepository.GetForActivityAsync(activityId, cancellationToken);
        var before = ActivitySnapshot.Serialize(activity, existingExpectedResults);

        // The activity, its follow-on writes and the audit row commit together (BLUM-8748).
        var result = await transactionRunner.RunAsync<ActivityException, Activity>(
            async token =>
            {
                try
                {
                    activity.UpdateDetails(request.Title, null, dateTimeProvider.GetUtcNow());

                    if (!activity.IsComplete)
                    {
                        activity.ChangeCategoryAndType(requestCategoryId, requestTaskId, request.CustomTaskText, dateTimeProvider.GetUtcNow());
                    }

                    if (status is not null)
                    {
                        activity.SetStatus(status, startMonth, startYear, dateTimeProvider.GetUtcNow(), closureReason);
                    }

                    if (completing)
                    {
                        var missing = CollectMissingFields(activity);
                        if (missing.Count > 0)
                        {
                            return new ActivityIncompleteException(activityId, missing);
                        }

                        activity.CompleteSetup(dateTimeProvider.GetUtcNow());
                    }

                    var saved = await activityRepository.UpdateAsync(activity, token);

                    await SyncExpectedResultsAsync(activityId, existingExpectedResults, expectedResultRows, actorId, token);

                    // Replace only the answers this request can legitimately assert about: the applicable questions
                    // (cleared/hidden ones are pruned) plus whatever it explicitly submitted. Answers for sections
                    // scoped to a different type — and orphaned answers whose question was since deleted — survive.
                    var replacedCodes = applicableQuestions.Select(q => q.Code)
                        .Concat(persistableAnswers.Select(p => p.Question.Code))
                        .ToHashSet(StringComparer.Ordinal);
                    await activityAnswerRepository.RemoveByActivityAsync(activityId, replacedCodes, token);
                    foreach (var (question, value) in persistableAnswers)
                    {
                        await activityAnswerRepository.AddAsync(
                            ActivityAnswer.Record(ActivityAnswerId.New(), activityId, question.Id, question.Code, value, actorId, dateTimeProvider.GetUtcNow()),
                            token);
                    }

                    var savedExpectedResults = await activityExpectedResultRepository.GetForActivityAsync(activityId, token);
                    await auditLogRepository.AppendAsync(
                        AuditLog.Create(
                            actorId,
                            AuditTargetType.Activity,
                            saved.Id.Value,
                            completing ? AuditAction.ActivityCompleted : AuditAction.ActivityUpdated,
                            before: before,
                            after: ActivitySnapshot.Serialize(saved, savedExpectedResults),
                            organisationId: organisationId),
                        token);

                    return saved;
                }
                catch (Exception exception)
                {
                    return new ActivityUnknownException(activityId, exception);
                }
            },
            cancellationToken);

        // Changes to a completed record (status, key results, completion itself) alter what the quarterly
        // report asks — in-flight reports must be reviewed (BLUM-6611). Draft edits don't affect the list.
        // After the commit: it is best-effort and swallows its own failures, which inside the transaction would
        // abort it and take the change with it.
        if (result.Match(saved => saved.IsComplete, _ => false))
        {
            await submissionReviewService.FlagOpenSubmissionsAsync(organisationId, cancellationToken);
        }

        return result;
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
        var plan = ExpectedResultSync.Plan(existing, expectedResults, activityId, actorId, dateTimeProvider.GetUtcNow());

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