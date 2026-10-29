using Application.Collections.Audit;
using Application.Collections.Exceptions;
using Application.Common.Features;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Domain.AuditLogs;
using Domain.Collections;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using MediatR;

namespace Application.Collections.Commands.RevertToDraftCollection;

// Reverts an Open collection back to Draft so its setup can be edited again. Admin-only. Only an Open
// collection can be reverted. RevertToDraft() flips IsDraft and clears IsManuallyClosed; the assigned
// Local Authorities (and other satellites) are left untouched.
public record RevertToDraftCollectionCommand : IRequest<Either<CollectionException, Collection>>
{
    public Guid Id { get; init; }
}

public class RevertToDraftCollectionCommandHandler(
    ICollectionQueries collectionQueries,
    ICollectionRepository collectionRepository,
    IAuditLogRepository auditLogRepository,
    ITransactionRunner transactionRunner,
    IFormModuleVersionQueries formModuleVersionQueries,
    IFeatureFlags featureFlags,
    PermissionsService permissionsService,
    IDateTimeProvider dateTimeProvider)
    : IRequestHandler<RevertToDraftCollectionCommand, Either<CollectionException, Collection>>
{
    public async Task<Either<CollectionException, Collection>> Handle(
        RevertToDraftCollectionCommand request,
        CancellationToken cancellationToken)
    {
        return await permissionsService.GetUserId().MatchAsync<Either<CollectionException, Collection>>(
            actorId => HandleWithActor(request, actorId, cancellationToken),
            error => new CollectionForbiddenException(new CollectionId(request.Id), error.Message));
    }

    private async Task<Either<CollectionException, Collection>> HandleWithActor(
        RevertToDraftCollectionCommand request,
        Domain.Users.UserId actorId,
        CancellationToken cancellationToken)
    {
        var utcNow = dateTimeProvider.GetUtcNow();

        var collectionId = new CollectionId(request.Id);

        var lookup = await collectionQueries.GetByIdAsync(collectionId, includeDeleted: false, cancellationToken);
        if (lookup.IsNone)
        {
            return new CollectionDoesNotExistException(collectionId);
        }

        var collection = lookup.ValueUnsafe();
        var status = collection.GetStatus(utcNow);
        if (!status.Equals(CollectionStatus.Open))
        {
            return new CollectionInvalidTransitionException(collectionId, status, CollectionStatuses.Draft);
        }

        // The change and its audit row commit together (BLUM-8748).
        return await transactionRunner.RunAsync<CollectionException, Collection>(
            async token =>
            {
                try
                {
                    // BLUM-9106: with versioning on, the row records the versions the collection keeps.
                    var recordVersions = featureFlags.IsEnabled(Feature.BLUM_9087);
                    var before = CollectionSnapshot.Serialize(
                        collection,
                        utcNow,
                        recordVersions ? await CollectionSnapshot.VersionNumbersAsync(collection, formModuleVersionQueries, token) : null);

                    // BLUM-9103: the collection keeps its recorded versions. Its window has opened, so they are fixed,
                    // and a report that has started (BLUM-9104) reads its questions from them.
                    collection.RevertToDraft(actorId, utcNow);
                    await collectionRepository.UpdateAsync(collection, token);

                    await auditLogRepository.AppendAsync(
                        AuditLog.Create(
                            actorId,
                            AuditTargetType.Collection,
                            collection.Id.Value,
                            AuditAction.CollectionRevertedToDraft,
                            before: before,
                            after: CollectionSnapshot.Serialize(
                                collection,
                                utcNow,
                                recordVersions ? await CollectionSnapshot.VersionNumbersAsync(collection, formModuleVersionQueries, token) : null),
                            payloadVersion: recordVersions ? CollectionSnapshot.FormVersionsPayloadVersion : AuditLog.InitialPayloadVersion),
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