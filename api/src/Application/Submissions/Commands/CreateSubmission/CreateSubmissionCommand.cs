using Application.Common.Features;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.Submissions.Audit;
using Application.Submissions.Exceptions;
using Domain.AuditLogs;
using Domain.Collections;
using Domain.DataCollectionForms;
using Domain.Submissions;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using MediatR;

namespace Application.Submissions.Commands.CreateSubmission;

// "Create report" — starts the LA's quarterly-report submission against an open assigned collection, snapshotting
// one task-list section per form module currently assigned to the collection.
public record CreateSubmissionCommand : IRequest<Either<SubmissionException, Submission>>
{
    public Guid CollectionId { get; init; }
}

public class CreateSubmissionCommandHandler(
    ISubmissionRepository submissionRepository,
    ISubmissionQueries submissionQueries,
    ICollectionQueries collectionQueries,
    ICollectionRepository collectionRepository,
    IOrganisationQueries organisationQueries,
    IFormModuleQueries formModuleQueries,
    IAuditLogRepository auditLogRepository,
    ITransactionRunner transactionRunner,
    PermissionsService permissionsService,
    IFeatureFlags featureFlags,
    IDateTimeProvider dateTimeProvider)
    : IRequestHandler<CreateSubmissionCommand, Either<SubmissionException, Submission>>
{
    public async Task<Either<SubmissionException, Submission>> Handle(
        CreateSubmissionCommand request,
        CancellationToken cancellationToken)
    {
        return await permissionsService.GetOrganisationPermissions().MatchAsync<Either<SubmissionException, Submission>>(
            permission => HandleWithPermission(request, permission, cancellationToken),
            error => new SubmissionForbiddenException(SubmissionId.Empty(), error.Message));
    }

    private async Task<Either<SubmissionException, Submission>> HandleWithPermission(
        CreateSubmissionCommand request,
        Permission permission,
        CancellationToken cancellationToken)
    {
        var utcNow = dateTimeProvider.GetUtcNow();

        if (permission.OrganisationId.IsNone || permission.UserId.IsNone)
        {
            return new SubmissionForbiddenException(SubmissionId.Empty(), "You are not associated with an organisation.");
        }

        var organisationId = permission.OrganisationId.ValueUnsafe();
        var actorId = permission.UserId.ValueUnsafe();
        var collectionId = new CollectionId(request.CollectionId);

        var organisationLookup = await organisationQueries.GetOrganisationById(organisationId, cancellationToken);
        if (organisationLookup.IsNone)
        {
            return new SubmissionCollectionNotEligibleException(collectionId);
        }

        var organisation = organisationLookup.ValueUnsafe();

        // The submission and its audit row commit together (BLUM-8748).
        return await transactionRunner.RunAsync<SubmissionException, Submission>(
            async token =>
            {
                // With versioning on, the collection is read under its row lock (BLUM-9084). A change to its forms or
                // recorded versions takes the same lock to check that no report has started, so a change already under
                // way commits before this reads the forms, and one that comes later sees this report.
                if (featureFlags.IsEnabled(Feature.BLUM_9087))
                {
                    await collectionRepository.LockForReportStartAsync(collectionId, token);
                }

                var collectionLookup = await collectionQueries.GetByIdAsync(collectionId, cancellationToken: token);
                if (collectionLookup.IsNone)
                {
                    return new SubmissionCollectionNotEligibleException(collectionId);
                }

                var collection = collectionLookup.ValueUnsafe();
                if (!SubmissionEligibility.IsQuarterly(collection) || !SubmissionEligibility.IsAssignedTo(collection, organisation))
                {
                    return new SubmissionCollectionNotEligibleException(collectionId);
                }

                if (!SubmissionEligibility.IsOpen(collection, utcNow))
                {
                    return new SubmissionCollectionNotOpenException(collectionId, collection.GetStatus(utcNow));
                }

                var existing = await submissionQueries.GetByCollectionAndOrgAsync(collectionId, organisationId, token);
                if (existing.IsSome)
                {
                    return new SubmissionAlreadyExistsException(existing.ValueUnsafe().Id);
                }

                IReadOnlyList<(FormModuleId ModuleId, string ModuleCode)> modules;
                try
                {
                    modules = await ResolveAssignedModulesAsync(collection, token);
                }
                catch (Exception exception)
                {
                    return new SubmissionUnknownException(SubmissionId.Empty(), exception);
                }

                try
                {
                    var submission = Submission.Start(SubmissionId.New(), collectionId, organisationId, modules, actorId, utcNow);
                    var saved = await submissionRepository.AddAsync(submission, token);

                    await auditLogRepository.AppendAsync(
                        AuditLog.Create(
                            actorId,
                            AuditTargetType.Submission,
                            saved.Id.Value,
                            AuditAction.SubmissionCreated,
                            before: null,
                            after: SubmissionSnapshot.Serialize(saved),
                            organisationId: organisationId),
                        token);

                    return saved;
                }
                catch (Exception exception)
                {
                    return new SubmissionUnknownException(SubmissionId.Empty(), exception);
                }
            },
            cancellationToken);
    }

    private async Task<IReadOnlyList<(FormModuleId ModuleId, string ModuleCode)>> ResolveAssignedModulesAsync(
        Collection collection,
        CancellationToken cancellationToken)
    {
        var allModules = await formModuleQueries.GetAllActiveAsync(cancellationToken);
        var byId = allModules.ToDictionary(m => m.Id, m => m.Code);

        return collection.Forms
            .Where(f => byId.ContainsKey(f.FormModuleId))
            .Select(f => (f.FormModuleId, byId[f.FormModuleId]))
            .ToList();
    }
}