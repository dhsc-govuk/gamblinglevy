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

namespace Application.Collections.Commands.SendManualReminder;

// Sends an ad-hoc reminder email to the Local Authorities assigned to a collection. Admin-only. The
// collection itself is not mutated. Resolving LA ids to mailbox addresses is a later concern — for now the
// recipient list carries the LocalAuthority ids as placeholders.
public record SendManualReminderCommand : IRequest<Either<CollectionException, Collection>>
{
    public Guid Id { get; init; }
}

public class SendManualReminderCommandHandler(
    ICollectionQueries collectionQueries,
    IEmailService emailService,
    IAuditLogRepository auditLogRepository,
    PermissionsService permissionsService)
    : IRequestHandler<SendManualReminderCommand, Either<CollectionException, Collection>>
{
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
        var collectionId = new CollectionId(request.Id);

        var lookup = await collectionQueries.GetByIdAsync(collectionId, includeDeleted: false, cancellationToken);
        if (lookup.IsNone)
        {
            return new CollectionDoesNotExistException(collectionId);
        }

        var collection = lookup.ValueUnsafe();

        try
        {
            // Placeholder recipients: the assigned LA ids. Resolving these to mailbox addresses is out of scope here.
            var recipients = collection.LocalAuthorities
                .Select(la => la.LocalAuthorityId.Value.ToString())
                .ToList();

            var message = new EmailMessage(
                recipients,
                $"Reminder: {collection.Name}",
                $"This is a reminder to submit your response for the \"{collection.Name}\" data collection by {collection.SubmissionDeadline:d MMMM yyyy}.");

            await emailService.SendAsync(message, cancellationToken);

            await auditLogRepository.AppendAsync(
                AuditLog.Create(
                    actorId,
                    AuditTargetType.Collection,
                    collection.Id.Value,
                    AuditAction.CollectionReminderSent,
                    before: null,
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