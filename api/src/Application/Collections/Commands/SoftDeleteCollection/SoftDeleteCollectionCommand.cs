using Application.Collections.Audit;
using Application.Collections.Exceptions;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Domain.AuditLogs;
using Domain.Collections;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using MediatR;

namespace Application.Collections.Commands.SoftDeleteCollection;

// Soft-deletes a collection (sets DeletedAt) so it can later be restored. Admin-only. We load excluding
// already-deleted records, so a missing record yields CollectionDoesNotExistException. SoftDelete() guards
// against double-deletion (Domain CollectionAlreadyDeletedException) — caught and surfaced as Unknown.
public record SoftDeleteCollectionCommand : IRequest<Either<CollectionException, Collection>>
{
    public Guid Id { get; init; }
}

public class SoftDeleteCollectionCommandHandler(
    ICollectionQueries collectionQueries,
    ICollectionRepository collectionRepository,
    IAuditLogRepository auditLogRepository,
    ITransactionRunner transactionRunner,
    PermissionsService permissionsService,
    IDateTimeProvider dateTimeProvider)
    : IRequestHandler<SoftDeleteCollectionCommand, Either<CollectionException, Collection>>
{
    public async Task<Either<CollectionException, Collection>> Handle(
        SoftDeleteCollectionCommand request,
        CancellationToken cancellationToken)
    {
        return await permissionsService.GetUserId().MatchAsync<Either<CollectionException, Collection>>(
            actorId => HandleWithActor(request, actorId, cancellationToken),
            error => new CollectionForbiddenException(new CollectionId(request.Id), error.Message));
    }

    private async Task<Either<CollectionException, Collection>> HandleWithActor(
        SoftDeleteCollectionCommand request,
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

        // The change and its audit row commit together (BLUM-8748).
        return await transactionRunner.RunAsync<CollectionException, Collection>(
            async token =>
            {
                try
                {
                    var utcNow = dateTimeProvider.GetUtcNow();
                    var before = CollectionSnapshot.Serialize(collection, utcNow);

                    collection.SoftDelete(actorId, utcNow);
                    await collectionRepository.UpdateAsync(collection, token);

                    await auditLogRepository.AppendAsync(
                        AuditLog.Create(
                            actorId,
                            AuditTargetType.Collection,
                            collection.Id.Value,
                            AuditAction.CollectionDeleted,
                            before: before,
                            after: CollectionSnapshot.Serialize(collection, utcNow)),
                        token);

                    return collection;
                }
                catch (Exception exception)
                {
                    return new CollectionUnknownException(collectionId, exception);
                }
            },
            cancellationToken);
    }
}