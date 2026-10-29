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
using Microsoft.Extensions.Logging;

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
    ITransactionRunner transactionRunner,
    ICollectionClosureNotifier closureNotifier,
    PermissionsService permissionsService,
    IDateTimeProvider dateTimeProvider,
    ILogger<CloseCollectionCommandHandler> logger)
    : IRequestHandler<CloseCollectionCommand, Either<CollectionException, Collection>>
{
    // Whole-operation ceiling for the closure notification, four times NotifyEmailServiceBase's per-send timeout.
    // Generous enough that a healthy Notify (sub-second sends) never reaches it whatever the admin count, tight
    // enough that a dead one cannot hold the request scope indefinitely. See the call site for why it is not the
    // request's own token.
    private static readonly TimeSpan NotifyBudget = TimeSpan.FromSeconds(60);

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
            return new CollectionInvalidTransitionException(collectionId, status, CollectionStatuses.Closed);
        }

        // The close and its audit row commit together (BLUM-8748).
        var closed = await transactionRunner.RunAsync<CollectionException, Collection>(
            async token =>
            {
                try
                {
                    var before = CollectionSnapshot.Serialize(collection, utcNow);

                    collection.Close(actorId, utcNow);
                    await collectionRepository.UpdateAsync(collection, token);

                    await auditLogRepository.AppendAsync(
                        AuditLog.Create(
                            actorId,
                            AuditTargetType.Collection,
                            collection.Id.Value,
                            AuditAction.CollectionClosed,
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

        if (closed.IsLeft)
        {
            return closed;
        }

        // Deliberately OUTSIDE the transaction above (BLUM-7719). The close is committed and audited by this point, so
        // a Notify blip folded into CollectionUnknownException would 500 a request that actually succeeded — the
        // same best-effort contract every other email path in this codebase keeps. What the sweep does with this
        // collection afterwards is the notifier's decision, not this handler's: a send that failed is stamped and
        // never retried, while an unconfigured Notify template deliberately is not, so the backlog goes out once
        // DHSC creates it.
        //
        // Bounded, but NOT linked to the request's token. A caller who drops the socket must not cancel a loop
        // that has already mailed some admins and not yet written the marker — the next sweep would then mail all
        // of them again, the exact duplicate the marker exists to prevent. The budget guards the opposite failure:
        // the sends are sequential and each burns NotifyEmailServiceBase's 15s timeout when Notify is down, so an
        // unbounded loop would hold this request's scope, DbContext and pooled connection for one timeout per
        // active DHSC admin. Whatever the budget cuts short is recorded as a failed send and the marker is still
        // written, so nothing is re-mailed.
        using var notifyBudget = new CancellationTokenSource(NotifyBudget);
        try
        {
            await closureNotifier.NotifyAsync(collection, notifyBudget.Token);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Closure notification failed for data collection {CollectionId}; the collection is still closed.",
                collection.Id.Value);
        }

        return collection;
    }
}