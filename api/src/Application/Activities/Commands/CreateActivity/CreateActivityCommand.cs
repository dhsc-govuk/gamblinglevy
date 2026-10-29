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
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using MediatR;

namespace Application.Activities.Commands.CreateActivity;

public record CreateActivityCommand : IRequest<Either<ActivityException, Activity>>
{
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

public class CreateActivityCommandHandler(
    IActivityRepository activityRepository,
    IActivityExpectedResultRepository activityExpectedResultRepository,
    IActivityAnswerRepository activityAnswerRepository,
    IFormSchemaQueries formSchemaQueries,
    IOrganisationQueries organisationQueries,
    ICategoryQueries categoryQueries,
    IKeyResultQueries keyResultQueries,
    IAuditLogRepository auditLogRepository,
    ITransactionRunner transactionRunner,
    SubmissionReviewService submissionReviewService,
    PermissionsService permissionsService,
    IDateTimeProvider dateTimeProvider,
    IFeatureFlags featureFlags)
    : IRequestHandler<CreateActivityCommand, Either<ActivityException, Activity>>
{
    public async Task<Either<ActivityException, Activity>> Handle(
        CreateActivityCommand request,
        CancellationToken cancellationToken)
    {
        return await permissionsService.GetOrganisationPermissions().MatchAsync<Either<ActivityException, Activity>>(
            permission => HandleWithPermission(request, permission, cancellationToken),
            error => new ActivityForbiddenException(ActivityId.Empty(), error.Message));
    }

    private async Task<Either<ActivityException, Activity>> HandleWithPermission(
        CreateActivityCommand request,
        Permission permission,
        CancellationToken cancellationToken)
    {
        if (permission.OrganisationId.IsNone || permission.UserId.IsNone)
        {
            return new ActivityForbiddenException(ActivityId.Empty(), "You are not associated with an organisation.");
        }

        var organisationId = permission.OrganisationId.ValueUnsafe();
        var actorId = permission.UserId.ValueUnsafe();

        var orgLookup = await organisationQueries.GetOrganisationById(organisationId, cancellationToken);
        if (orgLookup.IsNone)
        {
            return new ActivityForbiddenException(ActivityId.Empty(), "Your organisation could not be found.");
        }

        var activityId = ActivityId.New();
        var categoryId = request.CategoryId.HasValue ? new CategoryId(request.CategoryId.Value) : null;
        var taskId = request.TaskId.HasValue ? new ActivityTaskId(request.TaskId.Value) : null;
        var status = string.IsNullOrWhiteSpace(request.Status) ? null : ActivityStatus.From(request.Status);
        var startMonth = status?.IsActive == true ? request.StartMonth : null;
        var startYear = status?.IsActive == true ? request.StartYear : null;
        var closureReason = status?.IsClosed == true ? request.ClosureReason : null;

        // Validate dynamic-question answers against the active form schema scoped to the chosen category,
        // before any save. Required answers are only enforced when not saving a draft, and only for questions
        // applicable to the chosen type (sections scoped to another type never block); type/option errors block
        // for every schema question, and every valid schema answer is persisted — so answers submitted for a
        // de-scoped section (e.g. by a stale client) are kept rather than silently dropped.
        var schema = await formSchemaQueries.GetActiveSchemaForCategoryAsync(categoryId, cancellationToken);
        var applicableQuestions = schema.QuestionsForTask(taskId);
        var inapplicableQuestions = schema.Questions.Except(applicableQuestions).ToList();
        var answersMap = BuildAnswersMap(request.Answers);

        // One visibility over the whole schema for both halves and the save, so a rule that points across the split by
        // activity type is judged the same way by each (BLUM_9087 makes it transitive).
        var visibility = ActivityAnswerValidator.Visibility(schema.Questions, answersMap, featureFlags.IsEnabled(Feature.BLUM_9087));
        var answerErrors = ActivityAnswerValidator.Validate(applicableQuestions, answersMap, !request.SaveAsDraft, visibility)
            .Concat(ActivityAnswerValidator.Validate(inapplicableQuestions, answersMap, enforceRequired: false, visibility))
            .ToList();
        if (answerErrors.Count > 0)
        {
            return new ActivityAnswersInvalidException(activityId, answerErrors);
        }

        var persistableAnswers = ActivityAnswerValidator.CollectPersistable(schema.Questions, answersMap, visibility);

        // Key results: standard selections must belong to the chosen type; the per-category minimum is only
        // enforced on completion and never for People-and-roles (which has no key results).
        var definitions = taskId is null
            ? []
            : await keyResultQueries.GetDefinitionsForTaskAsync(taskId, cancellationToken);
        var (expectedResultRows, expectedResultsError) = ExpectedResultInputs.Normalise(request.ExpectedResults, definitions);
        if (expectedResultsError is not null)
        {
            return new ActivityExpectedResultsInvalidException(activityId, expectedResultsError);
        }

        if (!request.SaveAsDraft)
        {
            var (nameError, minimumError) = await ExpectedResultInputs.ValidateCompletionAsync(
                categoryQueries, categoryId, request.Title, definitions.Count, expectedResultRows.Count, cancellationToken);
            if (nameError is not null)
            {
                return new ActivityNameRequiredException(activityId, nameError);
            }

            if (minimumError is not null)
            {
                return new ActivityExpectedResultsInvalidException(activityId, minimumError);
            }
        }

        // The activity, its follow-on writes and the audit row commit together (BLUM-8748).
        var result = await transactionRunner.RunAsync<ActivityException, Activity>(
            async token =>
            {
                try
                {
                    var activity = request.SaveAsDraft
                        ? Activity.NewDraft(activityId, organisationId, actorId, dateTimeProvider.GetUtcNow(), request.Title, categoryId, taskId, request.CustomTaskText, status, startMonth, startYear, closureReason)
                        : Activity.NewComplete(activityId, organisationId, actorId, categoryId!, taskId!, status!, dateTimeProvider.GetUtcNow(), request.Title, request.CustomTaskText, startMonth, startYear, closureReason);

                    var saved = await activityRepository.AddAsync(activity, token);

                    await PersistExpectedResultsAsync(activityId, expectedResultRows, actorId, token);

                    foreach (var (question, value) in persistableAnswers)
                    {
                        await activityAnswerRepository.AddAsync(
                            ActivityAnswer.Record(ActivityAnswerId.New(), activityId, question.Id, question.Code, value, actorId, dateTimeProvider.GetUtcNow()),
                            token);
                    }

                    var expectedResults = await activityExpectedResultRepository.GetForActivityAsync(activityId, token);
                    await auditLogRepository.AppendAsync(
                        AuditLog.Create(
                            actorId,
                            AuditTargetType.Activity,
                            saved.Id.Value,
                            AuditAction.ActivityCreated,
                            before: null,
                            after: ActivitySnapshot.Serialize(saved, expectedResults),
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

        // A new completed record changes the reportable activity list — in-flight quarterly reports must be
        // reviewed (BLUM-6611). Draft creation doesn't affect the list.
        // After the commit: it is best-effort and swallows its own failures, which inside the transaction would
        // abort it and take the change with it.
        if (result.Match(saved => saved.IsComplete, _ => false))
        {
            await submissionReviewService.FlagOpenSubmissionsAsync(organisationId, cancellationToken);
        }

        return result;
    }

    private async Task PersistExpectedResultsAsync(
        ActivityId activityId,
        IReadOnlyList<(string? KeyResultCode, string Text)> expectedResults,
        Domain.Users.UserId actorId,
        CancellationToken cancellationToken)
    {
        var order = 0;
        foreach (var (keyResultCode, text) in expectedResults)
        {
            await activityExpectedResultRepository.AddAsync(
                ActivityExpectedResult.Record(ActivityExpectedResultId.New(), activityId, text, order++, actorId, dateTimeProvider.GetUtcNow(), keyResultCode),
                cancellationToken);
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