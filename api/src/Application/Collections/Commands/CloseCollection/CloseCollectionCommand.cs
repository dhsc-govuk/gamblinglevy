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

namespace Application.Collections.Commands.CloseCollection;

// Manually closes an Open collection ahead of its submission deadline. Admin-only. The status is derived
// (Collection.GetStatus()) so only an Open collection can be closed; Close() sets IsManuallyClosed.
public record CloseCollectionCommand : IRequest<Either<CollectionException, Collection>>
{
    public Guid Id { get; init; }
}

public class CloseCollectionCommandHandler(
    ICollectionQueries collectionQueries,
    ICollectionRepository collectionRepository,
    IAuditLogRepository auditLogRepository,
    PermissionsService permissionsService)
    : IRequestHandler<CloseCollectionCommand, Either<CollectionException, Collection>>
{
    public async Task<Either<CollectionException, Collection>> Handle(
        CloseCollectionCommand request,
        CancellationToken cancellationToken)
    {
        return await permissionsService.GetUserId().MatchAsync<Either<CollectionException, Collection>>(
            actorId => HandleWithActor(request, actorId, cancellationToken),
            error => new CollectionForbiddenException(new CollectionId(request.Id), error.Message));
    }

    private async Task<Either<CollectionException, Collection>> HandleWithActor(
        CloseCollectionCommand request,
        Domain.Users.UserId actorId,
        CancellationToken cancellationToken)
    {
        var collectionId = new CollectionId(request.Id);

        var lookup = await collectionQueries.GetByIdAsync(collectionId, includeDeleted: false, cancellationToken);
        if (lookup.IsNone)
        {
            return new CollectionDoesNotExistException(collectionId);
        }

        var collection = lookup.ValueUnsafe();
        var status = collection.GetStatus();
        if (!status.Equals(CollectionStatus.Open))
        {
            return new CollectionInvalidTransitionException(collectionId, status, CollectionStatuses.Closed);
        }

        try
        {
            var before = CollectionSnapshot.Serialize(collection);

            collection.Close(actorId);
            await collectionRepository.UpdateAsync(collection, cancellationToken);

            await auditLogRepository.AppendAsync(
                AuditLog.Create(
                    actorId,
                    AuditTargetType.Collection,
                    collection.Id.Value,
                    AuditAction.CollectionClosed,
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