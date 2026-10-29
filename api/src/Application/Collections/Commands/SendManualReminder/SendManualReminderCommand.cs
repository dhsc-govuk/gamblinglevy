using Application.Collections.Audit;
using Application.Collections.Exceptions;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.Common.Settings;
using Domain.AuditLogs;
using Domain.Collections;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Collections.Commands.SendManualReminder;

// AC1's manual branch (BLUM-7571): sends the deadline reminder immediately, outside the configured cadence, to
// every assigned organisation that has not yet submitted — plus the summary to DHSC Super Admins. The collection
// itself is not mutated; the send is recorded as a row in collection_reminder_dispatches with trigger "manual".
//
// It shares CollectionReminderNotifier's dispatch path with the background sweep on purpose: AC3's
// already-submitted filter and the "submitted by all LAs" resolution then hold on both by construction rather
// than by two implementations happening to agree. The previous version of this handler is what that is guarding
// against — it built its recipient list from the assignment rows' GUIDs, stringified, and posted them to the
// generic IEmailService (a no-op logger), which meant it reached nobody at all on an all-LAs window and never
// filtered out anyone who had already submitted.
public record SendManualReminderCommand : IRequest<Either<CollectionException, Collection>>
{
    public Guid Id { get; init; }
}

public class SendManualReminderCommandHandler(
    ICollectionQueries collectionQueries,
    ICollectionReminderNotifier reminderNotifier,
    IAuditLogRepository auditLogRepository,
    IDateTimeProvider dateTimeProvider,
    ApplicationSettings settings,
    PermissionsService permissionsService,
    ILogger<SendManualReminderCommandHandler> logger)
    : IRequestHandler<SendManualReminderCommand, Either<CollectionException, Collection>>
{
    // The ceiling sits under Azure App Service's 230-second idle timeout, the same platform constant the bulk
    // import is sized against. The floor stops a misconfigured setting cancelling the fan-out before it starts.
    private const int MinBudgetSeconds = 15;
    private const int MaxBudgetSeconds = 200;

    public async Task<Either<CollectionException, Collection>> Handle(
        SendManualReminderCommand request,
        CancellationToken cancellationToken)
    {
        return await permissionsService.GetUserId().MatchAsync<Either<CollectionException, Collection>>(
            actorId => HandleWithActor(request, actorId, cancellationToken),
            error => new CollectionForbiddenException(new CollectionId(request.Id), error.Message));
    }

    private async Task<Either<CollectionException, Collection>> HandleWithActor(
        SendManualReminderCommand request,
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

        // Defence in depth rather than a new user-facing path: the View screen already hides "Send manual
        // reminder" unless the collection is open. Reminding people to submit to a draft, scheduled or closed
        // window is meaningless, and on a closed one it is actively wrong.
        var status = collection.GetStatus(utcNow);
        if (!status.Equals(CollectionStatus.Open))
        {
            return new CollectionInvalidTransitionException(collectionId, status.ToString(), CollectionStatuses.Open);
        }

        try
        {
            // A fresh CancellationTokenSource, NOT linked to the request token. An admin who presses the button
            // and then closes the tab must not cancel a loop that has already mailed some organisations and not
            // yet written the dispatch row that records it — the same reasoning CloseCollectionCommandHandler's
            // notify budget carries.
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(
                Math.Clamp(settings.CollectionNotifications.ManualReminderBudgetSeconds, MinBudgetSeconds, MaxBudgetSeconds)));

            var result = await reminderNotifier.SendManualAsync(collection, budget.Token);

            logger.LogInformation(
                "Manual reminder for data collection {CollectionId} reached {ProviderSent} contact(s) across {Outstanding} outstanding organisation(s), and {DhscSent} DHSC admin(s).",
                collection.Id.Value,
                result.ProviderEmailsSent,
                result.OutstandingOrganisations,
                result.DhscEmailsSent);
        }
        catch (Exception exception)
        {
            // Best-effort, like every other email path here: a failed fan-out must not 500 a request the admin
            // had every right to make. The dispatch row and this log are the record of what happened.
            logger.LogWarning(exception, "Manual reminder fan-out failed for data collection {CollectionId}.", collection.Id.Value);
        }

        try
        {
            // The audit row records that the button was pressed and by whom; what actually went out lives in
            // collection_reminder_dispatches, which is where BLUM-7578's delivery log will read it from.
            await auditLogRepository.AppendAsync(
                AuditLog.Create(
                    actorId,
                    AuditTargetType.Collection,
                    collection.Id.Value,
                    AuditAction.CollectionReminderSent,
                    before: null,
                    after: CollectionSnapshot.Serialize(collection, utcNow)),
                cancellationToken);

            return collection;
        }
        catch (Exception exception)
        {
            return new CollectionUnknownException(collectionId, exception);
        }
    }
}