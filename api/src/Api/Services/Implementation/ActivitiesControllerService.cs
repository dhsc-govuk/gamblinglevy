using Api.Dtos;
using Api.Services.Abstract;
using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Domain.Activities;
using Domain.Organisations;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;

namespace Api.Services.Implementation;

public class ActivitiesControllerService(
    IActivityQueries activityQueries,
    IActivityExpectedResultRepository activityExpectedResultRepository,
    IActivityAnswerRepository activityAnswerRepository,
    ICategoryQueries categoryQueries,
    IKeyResultQueries keyResultQueries) : IActivitiesControllerService
{
    public async Task<ActivityDto> ToDtoAsync(Activity activity, CancellationToken cancellationToken)
    {
        var categoryNames = await GetCategoryNamesAsync(cancellationToken);
        var taskNames = await GetTaskNamesAsync(cancellationToken);

        var expectedResults = await activityExpectedResultRepository.GetForActivityAsync(activity.Id, cancellationToken);
        var answers = await activityAnswerRepository.GetForActivityAsync(activity.Id, cancellationToken);

        var taskName = ResolveName(taskNames, activity.TaskId?.Value);

        return new ActivityDto(
            activity.Id.Value,
            activity.OrganisationId.Value,
            ResolveDisplayTitle(activity, taskName),
            activity.Title,
            activity.CategoryId?.Value,
            ResolveName(categoryNames, activity.CategoryId?.Value),
            activity.TaskId?.Value,
            taskName,
            activity.CustomTaskText,
            activity.Status?.ToString(),
            !activity.IsComplete,
            activity.StartMonth,
            activity.StartYear,
            expectedResults.Select(r => new ExpectedResultDto(r.KeyResultCode, r.Text)).ToList(),
            answers.Select(a => new ActivityAnswerDto(a.QuestionCode, a.Value)).ToList(),
            activity.CreatedAt,
            activity.UpdatedAt);
    }

    public async Task<IReadOnlyList<ActivitySummaryDto>> ListAsync(Permission permission, string? status, CancellationToken cancellationToken)
    {
        var organisationId = ResolveOrg(permission);
        if (organisationId is null)
        {
            return [];
        }

        var activities = await activityQueries.GetByOrganisationAsync(organisationId, status, cancellationToken);
        return await ToSummariesAsync(activities, cancellationToken);
    }

    public async Task<IReadOnlyList<ActivitySummaryDto>> ReviewAsync(Permission permission, CancellationToken cancellationToken)
    {
        var organisationId = ResolveOrg(permission);
        if (organisationId is null)
        {
            return [];
        }

        var activities = await activityQueries.GetActiveByOrganisationAsync(organisationId, cancellationToken);
        return await ToSummariesAsync(activities, cancellationToken);
    }

    public async Task<IReadOnlyList<ActivitySummaryDto>> DeletedAsync(Permission permission, CancellationToken cancellationToken)
    {
        var organisationId = ResolveOrg(permission);
        if (organisationId is null)
        {
            return [];
        }

        var activities = await activityQueries.GetDeletedByOrganisationAsync(organisationId, cancellationToken);
        return await ToSummariesAsync(activities, cancellationToken);
    }

    public async Task<Option<ActivityDto>> GetAsync(Permission permission, Guid activityId, CancellationToken cancellationToken)
    {
        var organisationId = ResolveOrg(permission);
        if (organisationId is null)
        {
            return Option<ActivityDto>.None;
        }

        var lookup = await activityQueries.GetByIdForOrgAsync(new ActivityId(activityId), organisationId, cancellationToken: cancellationToken);
        if (lookup.IsNone)
        {
            return Option<ActivityDto>.None;
        }

        return await ToDtoAsync(lookup.ValueUnsafe(), cancellationToken);
    }

    public async Task<ActivityTaxonomyDto> GetTaxonomyAsync(CancellationToken cancellationToken)
    {
        var categories = await categoryQueries.GetActiveCategoriesAsync(cancellationToken);
        var tasks = await categoryQueries.GetActiveTasksAsync(cancellationToken);
        var keyResults = await keyResultQueries.GetAllDefinitionsAsync(cancellationToken);

        return new ActivityTaxonomyDto(
            categories.Select(c => new TaxonomyCategoryDto(c.Id.Value, c.Name)).ToList(),
            tasks.Select(t => new TaxonomyTaskDto(t.Id.Value, t.TaskName, t.CategoryId.Value)).ToList(),
            keyResults.Select(k => new TaxonomyKeyResultDto(k.Code, k.ActivityTaskId.Value, k.Label)).ToList());
    }

    private async Task<IReadOnlyList<ActivitySummaryDto>> ToSummariesAsync(
        IReadOnlyList<Activity> activities,
        CancellationToken cancellationToken)
    {
        if (activities.Count == 0)
        {
            return [];
        }

        var categoryNames = await GetCategoryNamesAsync(cancellationToken);
        var taskNames = await GetTaskNamesAsync(cancellationToken);

        return activities
            .Select(a =>
            {
                var taskName = ResolveName(taskNames, a.TaskId?.Value);
                return new ActivitySummaryDto(
                    a.Id.Value,
                    ResolveDisplayTitle(a, taskName),
                    a.CategoryId?.Value,
                    ResolveName(categoryNames, a.CategoryId?.Value),
                    a.TaskId?.Value,
                    taskName,
                    a.Status?.ToString(),
                    !a.IsComplete,
                    a.CreatedAt,
                    a.UpdatedAt);
            })
            .ToList();
    }

    // IsNone/ValueUnsafe, not Match: Option.Match runs LanguageExt's Check.NullReturn on the chosen branch, so a
    // null from the None arm throws ResultIsNullException instead of yielding null (BLUM-7216). Today the
    // Role.AnyOrganisationMember gate means every caller carries an organisation, so the None arm is unreachable —
    // but it is the arm the callers' own `is null` guards exist to serve, and it must not throw if it is ever hit.
    private static OrganisationId? ResolveOrg(Permission permission) =>
        permission.OrganisationId.IsNone ? null : permission.OrganisationId.ValueUnsafe();

    // The activity name is optional; the design falls back to the chosen type ("we'll use the type of work you
    // selected") and finally to the custom "Other" text.
    private static string ResolveDisplayTitle(Activity activity, string? taskName)
    {
        if (!string.IsNullOrWhiteSpace(activity.Title))
        {
            return activity.Title!;
        }

        if (!string.IsNullOrWhiteSpace(activity.CustomTaskText))
        {
            return activity.CustomTaskText!;
        }

        return string.IsNullOrWhiteSpace(taskName) ? "Untitled activity" : taskName!;
    }

    private async Task<Dictionary<Guid, string>> GetCategoryNamesAsync(CancellationToken cancellationToken)
    {
        var categories = await categoryQueries.GetActiveCategoriesAsync(cancellationToken);
        return categories.ToDictionary(c => c.Id.Value, c => c.Name);
    }

    private async Task<Dictionary<Guid, string>> GetTaskNamesAsync(CancellationToken cancellationToken)
    {
        var tasks = await categoryQueries.GetActiveTasksAsync(cancellationToken);
        return tasks.ToDictionary(t => t.Id.Value, t => t.TaskName);
    }

    private static string? ResolveName(IReadOnlyDictionary<Guid, string> lookup, Guid? id)
    {
        if (id is null)
        {
            return null;
        }

        return lookup.TryGetValue(id.Value, out var name) ? name : null;
    }
}