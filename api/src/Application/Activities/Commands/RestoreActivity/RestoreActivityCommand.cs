using Application.Activities.Audit;
using Application.Activities.Exceptions;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.Submissions;
using Domain.Activities;
using Domain.AuditLogs;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using MediatR;

namespace Application.Activities.Commands.RestoreActivity;

// Restores a soft-deleted activity back to the main list (BLUM redesign).
public record RestoreActivityCommand : IRequest<Either<ActivityException, Activity>>
{
    public Guid Id { get; init; }
}

public class RestoreActivityCommandHandler(
    IActivityRepository activityRepository,
    IActivityQueries activityQueries,
    IAuditLogRepository auditLogRepository,
    ITransactionRunner transactionRunner,
    SubmissionReviewService submissionReviewService,
    PermissionsService permissionsService,
    IDateTimeProvider dateTimeProvider)
    : IRequestHandler<RestoreActivityCommand, Either<ActivityException, Activity>>
{
    public async Task<Either<ActivityException, Activity>> Handle(
        RestoreActivityCommand request,
        CancellationToken cancellationToken)
    {
        return await permissionsService.GetOrganisationPermissions().MatchAsync<Either<ActivityException, Activity>>(
            permission => HandleWithPermission(request, permission, cancellationToken),
            error => new ActivityForbiddenException(new ActivityId(request.Id), error.Message));
    }

    private async Task<Either<ActivityException, Activity>> HandleWithPermission(
        RestoreActivityCommand request,
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

        var lookup = await activityQueries.GetByIdForOrgAsync(activityId, organisationId, includeDeleted: true, cancellationToken);
        if (lookup.IsNone)
        {
            return new ActivityDoesNotExistException(activityId);
        }

        var activity = lookup.ValueUnsafe();
        var before = ActivitySnapshot.Serialize(activity);

        // The change and its audit row commit together (BLUM-8748).
        var result = await transactionRunner.RunAsync<ActivityException, Activity>(
            async token =>
            {
                try
                {
                    activity.Restore(actorId, dateTimeProvider.GetUtcNow());
                    var saved = await activityRepository.UpdateAsync(activity, token);

                    await auditLogRepository.AppendAsync(
                        AuditLog.Create(
                            actorId,
                            AuditTargetType.Activity,
                            saved.Id.Value,
                            AuditAction.ActivityRestored,
                            before: before,
                            after: ActivitySnapshot.Serialize(saved),
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

        // Restoring a completed record changes the reportable activity list — in-flight quarterly reports must
        // be reviewed (BLUM-6611).
        // After the commit: it is best-effort and swallows its own failures, which inside the transaction would
        // abort it and take the change with it.
        if (result.Match(saved => saved.IsComplete, _ => false))
        {
            await submissionReviewService.FlagOpenSubmissionsAsync(organisationId, cancellationToken);
        }

        return result;
    }
}