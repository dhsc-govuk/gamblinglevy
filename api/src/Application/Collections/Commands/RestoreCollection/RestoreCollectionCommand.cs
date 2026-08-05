using Application.Collections.Audit;
using Application.Collections.Exceptions;
using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Domain.AuditLogs;
using Domain.Collections;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using MediatR;

namespace Application.Collections.Commands.RestoreCollection;

// Restores a previously soft-deleted collection (clears DeletedAt). Admin-only. Loaded with
// includeDeleted so a deleted record is found; a genuinely missing one yields CollectionDoesNotExistException.
// Restore() guards against restoring a non-deleted record (Domain CollectionNotDeletedException) — caught as Unknown.
public record RestoreCollectionCommand : IRequest<Either<CollectionException, Collection>>
{
    public Guid Id { get; init; }
}

public class RestoreCollectionCommandHandler(
    ICollectionQueries collectionQueries,
    ICollectionRepository collectionRepository,
    IAuditLogRepository auditLogRepository,
    PermissionsService permissionsService)
    : IRequestHandler<RestoreCollectionCommand, Either<CollectionException, Collection>>
{
    public async Task<Either<CollectionException, Collection>> Handle(
        RestoreCollectionCommand request,
        CancellationToken cancellationToken)
    {
        return await permissionsService.GetUserId().MatchAsync<Either<CollectionException, Collection>>(
            actorId => HandleWithActor(request, actorId, cancellationToken),
            error => new CollectionForbiddenException(new CollectionId(request.Id), error.Message));
    }

    private async Task<Either<CollectionException, Collection>> HandleWithActor(
        RestoreCollectionCommand request,
        Domain.Users.UserId actorId,
        CancellationToken cancellationToken)
    {
        var collectionId = new CollectionId(request.Id);

        var lookup = await collectionQueries.GetByIdAsync(collectionId, includeDeleted: true, cancellationToken);
        if (lookup.IsNone)
        {
            return new CollectionDoesNotExistException(collectionId);
        }

        var collection = lookup.ValueUnsafe();

        try
        {
            var before = CollectionSnapshot.Serialize(collection);

            collection.Restore(actorId);
            await collectionRepository.UpdateAsync(collection, cancellationToken);

            await auditLogRepository.AppendAsync(
                AuditLog.Create(
                    actorId,
                    AuditTargetType.Collection,
                    collection.Id.Value,
                    AuditAction.CollectionRestored,
                    before: before,
                    after: CollectionSnapshot.Serialize(collection)),
                cancellationToken);

            return collection;
        }
        catch (Exception exception)
        {
            return new CollectionUnknownException(collectionId, exception);
        }
    }
}