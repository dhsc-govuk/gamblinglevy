using Application.Activities.Audit;
using Application.Activities.Exceptions;
using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.Submissions;
using Domain.Activities;
using Domain.AuditLogs;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using MediatR;

namespace Application.Activities.Commands.SoftDeleteActivity;

// Soft-deletes an activity (BLUM redesign). The record is taken off the main list but retained so it can be
// restored from the "Deleted activities" page. Works for any non-deleted record (draft or completed).
public record SoftDeleteActivityCommand : IRequest<Either<ActivityException, Activity>>
{
    public Guid Id { get; init; }
}

public class SoftDeleteActivityCommandHandler(
    IActivityRepository activityRepository,
    IActivityQueries activityQueries,
    IAuditLogRepository auditLogRepository,
    SubmissionReviewService submissionReviewService,
    PermissionsService permissionsService)
    : IRequestHandler<SoftDeleteActivityCommand, Either<ActivityException, Activity>>
{
    public async Task<Either<ActivityException, Activity>> Handle(
        SoftDeleteActivityCommand request,
        CancellationToken cancellationToken)
    {
        return await permissionsService.GetOrganisationPermissions().MatchAsync<Either<ActivityException, Activity>>(
            permission => HandleWithPermission(request, permission, cancellationToken),
            error => new ActivityForbiddenException(new ActivityId(request.Id), error.Message));
    }

    private async Task<Either<ActivityException, Activity>> HandleWithPermission(
        SoftDeleteActivityCommand request,
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
        var before = ActivitySnapshot.Serialize(activity);

        try
        {
            activity.SoftDelete(actorId);
            var saved = await activityRepository.UpdateAsync(activity, cancellationToken);

            await auditLogRepository.AppendAsync(
                AuditLog.Create(
                    actorId,
                    AuditTargetType.Activity,
                    saved.Id.Value,
                    AuditAction.ActivityDeleted,
                    before: before,
                    after: ActivitySnapshot.Serialize(saved),
                    organisationId: organisationId),
                cancellationToken);

            // Deleting a completed record changes the reportable activity list — in-flight quarterly reports must
            // be reviewed (BLUM-6611).
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
}