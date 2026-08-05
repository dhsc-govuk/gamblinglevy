using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.Submissions.Exceptions;
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
    IOrganisationQueries organisationQueries,
    IFormModuleQueries formModuleQueries,
    PermissionsService permissionsService)
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
        if (permission.OrganisationId.IsNone || permission.UserId.IsNone)
        {
            return new SubmissionForbiddenException(SubmissionId.Empty(), "You are not associated with an organisation.");
        }

        var organisationId = permission.OrganisationId.ValueUnsafe();
        var actorId = permission.UserId.ValueUnsafe();
        var collectionId = new CollectionId(request.CollectionId);

        var collectionLookup = await collectionQueries.GetByIdAsync(collectionId, cancellationToken: cancellationToken);
        if (collectionLookup.IsNone)
        {
            return new SubmissionCollectionNotEligibleException(collectionId);
        }

        var organisationLookup = await organisationQueries.GetOrganisationById(organisationId, cancellationToken);
        if (organisationLookup.IsNone)
        {
            return new SubmissionCollectionNotEligibleException(collectionId);
        }

        var organisation = organisationLookup.ValueUnsafe();
        var collection = collectionLookup.ValueUnsafe();
        if (!SubmissionEligibility.IsQuarterly(collection) || !SubmissionEligibility.IsAssignedTo(collection, organisation))
        {
            return new SubmissionCollectionNotEligibleException(collectionId);
        }

        if (!SubmissionEligibility.IsOpen(collection))
        {
            return new SubmissionCollectionNotOpenException(collectionId, collection.GetStatus());
        }

        var existing = await submissionQueries.GetByCollectionAndOrgAsync(collectionId, organisationId, cancellationToken);
        if (existing.IsSome)
        {
            return new SubmissionAlreadyExistsException(existing.ValueUnsafe().Id);
        }

        try
        {
            var modules = await ResolveAssignedModulesAsync(collection, cancellationToken);
            var submission = Submission.Start(SubmissionId.New(), collectionId, organisationId, modules, actorId);
            var saved = await submissionRepository.AddAsync(submission, cancellationToken);
            return saved;
        }
        catch (Exception exception)
        {
            return new SubmissionUnknownException(SubmissionId.Empty(), exception);
        }
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